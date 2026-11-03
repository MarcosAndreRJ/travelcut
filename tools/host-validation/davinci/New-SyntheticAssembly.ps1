<#
.SYNOPSIS
  Gera um manifesto editor-assembly/1 de homologacao com 2 cortes de cada midia sintetica.

.DESCRIPTION
  - Entrada: synthetic-media.json (gerado por New-SyntheticMedia.ps1).
  - Saida: editor-assembly/1 com 4 clipes (2 cortes por midia), timeline 29,97 fps, fingerprint "harness".
  - Intervalos de origem em frames com FIM EXCLUSIVO (contrato): [start, end).
  - recordFrame relativo ao inicio da timeline; cortes colocados em sequencia.
  - Nome do projeto obrigatoriamente com prefixo TravelCut_.
  - Nao sobrescreve o arquivo de saida.

  Estado: escrito e revisado por leitura; NAO executado na VM (sem host DaVinci/FFmpeg).

.EXAMPLE
  .\New-SyntheticAssembly.ps1 -MediaManifestPath "$env:TEMP\TravelCutHarness\media\synthetic-media.json" -OutputPath "$env:TEMP\TravelCutHarness\assembly.json"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$MediaManifestPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$ProjectName = ('TravelCut_Harness_' + (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmss')),
    [string]$TimelineName = 'TravelCut Harness Timeline'
)

$ErrorActionPreference = 'Stop'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

if ($ProjectName -notmatch '^TravelCut_[A-Za-z0-9_\-]{1,140}$') {
    throw 'Nome de projeto invalido: use TravelCut_ seguido de letras, numeros, _ ou -.'
}
if (-not (Test-Path -LiteralPath $MediaManifestPath)) {
    throw 'Manifesto de midia nao encontrado. Gere com New-SyntheticMedia.ps1.'
}
if (Test-Path -LiteralPath $OutputPath) {
    throw 'Arquivo de saida ja existe e nao sera sobrescrito.'
}

$mediaDoc = Get-Content -LiteralPath $MediaManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($mediaDoc.schemaVersion -ne 'harness-media/1') {
    throw 'schemaVersion do manifesto de midia nao suportado.'
}

$fpsNum = 30000
$fpsDen = 1001
# Cortes em frames da FONTE, fim exclusivo: [inicio, fim)
$cutsPerMedia = @(
    @{ Start = 30;  EndExclusive = 150 },   # ~1,0 s a ~5,0 s
    @{ Start = 180; EndExclusive = 270 }    # ~6,0 s a ~9,0 s
)

function Convert-FramesToUs([long]$frames) {
    # frames * den / num segundos, em microssegundos
    return [long][math]::Round($frames * $fpsDen * 1000000.0 / $fpsNum)
}

$mediaList = New-Object System.Collections.ArrayList
$clipList = New-Object System.Collections.ArrayList
$recordFrame = [long]0
$mediaIndex = 0

foreach ($media in @($mediaDoc.media)) {
    $mediaIndex++
    $mediaId = [guid]::NewGuid().ToString()
    [void]$mediaList.Add([ordered]@{
        mediaId            = $mediaId
        relativePath       = $media.fileName
        absolutePath       = $media.absolutePath
        sourceFpsNum       = [long]$fpsNum
        sourceFpsDen       = [long]$fpsDen
        isVariableFrameRate = $false
        durationUs         = [long]([math]::Round([double]$media.durationSec * 1000000))
    })

    foreach ($cut in $cutsPerMedia) {
        $duration = [long]($cut.EndExclusive - $cut.Start)
        if ($duration -le 0) { throw 'Corte com intervalo vazio.' }
        [void]$clipList.Add([ordered]@{
            clipId                  = [guid]::NewGuid().ToString()
            mediaId                 = $mediaId
            trackIndex              = 1
            mediaType               = 'video_audio'
            sourceStartFrame        = [long]$cut.Start
            sourceEndFrameExclusive = [long]$cut.EndExclusive
            recordFrame             = $recordFrame
            durationFrames          = $duration
            sourceInUs              = (Convert-FramesToUs ([long]$cut.Start))
            sourceOutUs             = (Convert-FramesToUs ([long]$cut.EndExclusive))
        })
        $recordFrame += $duration
    }
}

$assembly = [ordered]@{
    schemaVersion       = 'editor-assembly/1'
    notice              = 'Manifesto de montagem de homologacao do TravelCut - nao e um projeto DaVinci.'
    projectId           = [guid]::NewGuid().ToString()
    planId              = [guid]::NewGuid().ToString()
    revision            = 1
    planFingerprint     = 'harness'
    editorProjectName   = $ProjectName
    timelineName        = $TimelineName
    timeBase            = [ordered]@{ frameDurationNum = [long]$fpsDen; frameDurationDen = [long]$fpsNum; label = '29,97 fps' }
    timelineFrameRate   = '29.97'
    media               = @($mediaList)
    clips               = @($clipList)
    rejected            = @()
    warnings            = @()
    assemblyFingerprint = 'harness'
}

$dir = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
New-Item -ItemType Directory -Force -Path $dir | Out-Null
[System.IO.File]::WriteAllText($OutputPath, ($assembly | ConvertTo-Json -Depth 8), $Utf8NoBom)

Write-Host "Manifesto de homologacao: $OutputPath"
Write-Host "Projeto (prefixo TravelCut_): $ProjectName"
Write-Host "Clipes: $($clipList.Count) (2 por midia); duracao total na timeline: $recordFrame frames"
