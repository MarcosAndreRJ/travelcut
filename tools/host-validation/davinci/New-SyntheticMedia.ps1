<#
.SYNOPSIS
  Gera 2 clipes sinteticos (H.264/AAC, 29,97 fps, 10 s) para homologacao do bridge DaVinci no host.

.DESCRIPTION
  - Usa FFmpeg (ffmpeg/ffprobe no PATH ou -FfmpegPath/-FfprobePath).
  - Video: padrao de teste com timecode queimado (%{pts:hms}) e numero do frame (%{frame_num}).
  - Audio: bipe de 0,1 s no inicio de cada segundo (alinhamento visual/auditivo no Resolve).
  - Nunca sobrescreve: se algum arquivo de saida existir, aborta.
  - A pasta de saida deve ficar SOB %TEMP% (ou usar -AllowNonTempDirectory explicitamente).
  - Gera synthetic-media.json com SHA-256, fps medido por ffprobe e duracao.

  Estado: script escrito na VM de desenvolvimento, NAO executado (sem FFmpeg na VM).
  Execucao real: host_pending (ver docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md).

.EXAMPLE
  .\New-SyntheticMedia.ps1 -OutputDirectory "$env:TEMP\TravelCutHarness\media-20261102"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$FfmpegPath = 'ffmpeg',
    [string]$FfprobePath = 'ffprobe',
    [string]$FontPath = (Join-Path $env:WINDIR 'Fonts\arial.ttf'),
    [switch]$AllowNonTempDirectory
)

$ErrorActionPreference = 'Stop'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$full = [System.IO.Path]::GetFullPath($OutputDirectory)
$tempRoot = [System.IO.Path]::GetFullPath($env:TEMP)
if (-not $AllowNonTempDirectory -and -not $full.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "A pasta de saida deve ficar sob '$tempRoot' (pasta temporaria). Use -AllowNonTempDirectory apenas se for consciente."
}

foreach ($tool in @($FfmpegPath, $FfprobePath)) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "Ferramenta nao encontrada: $tool. Instale FFmpeg (>= 5.1) ou informe o caminho."
    }
}
if (-not (Test-Path -LiteralPath $FontPath)) {
    throw "Fonte para o timecode nao encontrada. Informe -FontPath (ex.: uma .ttf do sistema)."
}

New-Item -ItemType Directory -Force -Path $full | Out-Null

# Caminho de fonte escapado para o filtergraph do FFmpeg (C:\x -> C\:/x).
$fontEscaped = ($FontPath -replace '\\', '/') -replace ':', '\:'

$clips = @(
    @{ File = 'synthetic_A_testsrc2_2997.mp4'; Source = 'testsrc2'; Tone = 1000 },
    @{ File = 'synthetic_B_testsrc_2997.mp4';  Source = 'testsrc';  Tone = 660 }
)

$manifestEntries = New-Object System.Collections.ArrayList

foreach ($clip in $clips) {
    $target = Join-Path $full $clip.File
    if (Test-Path -LiteralPath $target) {
        throw "Arquivo ja existe e nao sera sobrescrito: $($clip.File). Escolha outra pasta."
    }

    $draw = "drawtext=fontfile=$fontEscaped" +
            ":text='%{pts\:hms} F%{frame_num}'" +
            ':fontcolor=white:fontsize=64:box=1:boxcolor=black@0.6:x=40:y=40'
    $beep = "aevalsrc=exprs=0.5*sin(2*PI*$($clip.Tone)*t)*lt(mod(t\,1)\,0.1):s=48000:d=10"

    $ffArgs = @(
        '-hide_banner', '-loglevel', 'error', '-n',
        '-f', 'lavfi', '-i', "$($clip.Source)=size=1280x720:rate=30000/1001:duration=10",
        '-f', 'lavfi', '-i', $beep,
        '-filter:v', $draw,
        '-c:v', 'libx264', '-preset', 'veryfast', '-crf', '20', '-pix_fmt', 'yuv420p',
        '-r', '30000/1001',
        '-c:a', 'aac', '-b:a', '128k', '-ar', '48000', '-ac', '1',
        '-t', '10',
        '-movflags', '+faststart',
        $target
    )

    & $FfmpegPath @ffArgs | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "FFmpeg falhou ao gerar $($clip.File) (codigo $LASTEXITCODE). Verifique se o build tem drawtext/libx264."
    }

    $probeRaw = & $FfprobePath -v error -select_streams v:0 `
        -show_entries 'stream=codec_name,width,height,r_frame_rate,avg_frame_rate,nb_frames:format=duration' `
        -of json $target | Out-String
    $probe = $probeRaw | ConvertFrom-Json
    $stream = $probe.streams[0]
    $fpsOk = ($stream.r_frame_rate -eq '30000/1001')

    $hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    [void]$manifestEntries.Add([ordered]@{
        fileName    = $clip.File
        absolutePath = $target
        sha256      = $hash
        codec       = $stream.codec_name
        width       = $stream.width
        height      = $stream.height
        rFrameRate  = $stream.r_frame_rate
        avgFrameRate = $stream.avg_frame_rate
        nbFrames    = $stream.nb_frames
        durationSec = [double]$probe.format.duration
        fpsNum      = 30000
        fpsDen      = 1001
        fpsVerified = $fpsOk
    })

    if (-not $fpsOk) {
        throw "fps medido ($($stream.r_frame_rate)) diferente de 30000/1001 em $($clip.File). Nao use este conjunto para homologacao."
    }
}

$manifest = [ordered]@{
    schemaVersion = 'harness-media/1'
    generatedUtc  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    notice        = 'Midia sintetica de homologacao. Nao e midia pessoal.'
    media         = @($manifestEntries)
}

$manifestPath = Join-Path $full 'synthetic-media.json'
[System.IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), $Utf8NoBom)

Write-Host "Midia sintetica gerada em: $full"
Write-Host "Manifesto: $manifestPath"
Write-Host 'Confira no host: 2 arquivos, 29,97 fps (30000/1001) e bipe a cada segundo.'
