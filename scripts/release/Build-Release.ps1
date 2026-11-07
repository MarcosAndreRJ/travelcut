#Requires -Version 5.1
<#
.SYNOPSIS
    Gera o build Release x64 do TravelCut Studio e o pacote de teste para o host (pasta dist/).

.DESCRIPTION
    Etapas:
      a) checa dotnet (SDK 10+), node (22+) e npm; versoes vao para o log;
      b) npm ci (ou npm install, se nao houver package-lock.json) e npm run build em frontend/;
         falha se frontend/dist/index.html nao existir;
      c) dotnet test TravelCutStudio.sln -c Release (pulavel com -SkipTests, registrado no manifesto);
      d) dotnet publish de TravelCut.Desktop (app/) e TravelCut.Mcp (mcp/), win-x64, self-contained por padrao,
         sem PublishSingleFile (ver docs/14-BUILD-DEPLOY-HOST.md);
      e) copia workers, tools/host-validation (sem evidencias), docs e progresso/HOST_TESTS.md para o pacote;
      f) proibe no pacote midias, pesos, caches, bancos, venv, node_modules, .tcpart e evidencias (falha se houver);
      g) gera MANIFEST.json e SHA256SUMS.txt; h) gera LEIA-ME-HOST.md;
      i) com -Zip, gera o .zip do pacote e o hash SHA-256 do .zip.

    Nao assina binarios, nao instala nada globalmente, nao escreve em origem (1-Bruto/2-Assets) e nao copia para
    rede ou unidade Z:. Nenhum artefato e enviado a lugar nenhum.

.PARAMETER Configuration
    Somente Release.

.PARAMETER Runtime
    Somente win-x64.

.PARAMETER SelfContained
    $true (padrao) publica com o runtime .NET incluido. $false gera pacote framework-dependent (nome recebe sufixo -fdd).

.PARAMETER OutputRoot
    Pasta de saida. Relativa a raiz do repositorio. Padrao: dist (ignorada pelo git). Caminhos UNC sao recusados.

.PARAMETER SkipTests
    Pula dotnet test. O manifesto registra "executed": false.

.PARAMETER Zip
    Gera <pacote>.zip e <pacote>.zip.sha256 em OutputRoot.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\release\Build-Release.ps1 -Zip
#>
[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',

    [bool]$SelfContained = $true,

    [string]$OutputRoot = 'dist',

    [switch]$SkipTests,

    # Diagnostico: nao executa npm ci/install (usa node_modules existente). O pacote fica marcado como nao reprodutivel.
    [switch]$SkipFrontendInstall,

    # Diagnostico: nao executa npm run build e reaproveita frontend/dist existente. Marcado como nao reprodutivel.
    [switch]$SkipFrontendBuild,

    [switch]$Zip
)

$ErrorActionPreference = 'Stop'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$StartedUtc = [DateTime]::UtcNow

# ---------------------------------------------------------------------------
# Caminhos
# ---------------------------------------------------------------------------
$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))

if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutDir = [System.IO.Path]::GetFullPath($OutputRoot)
}
else {
    $OutDir = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $OutputRoot))
}
if ($OutDir.StartsWith('\\')) {
    throw 'OutputRoot em caminho de rede (UNC) recusado. Copie o pacote manualmente apos autorizacao.'
}

$PropsPath = Join-Path $RepoRoot 'Directory.Build.props'
[xml]$PropsXml = Get-Content -LiteralPath $PropsPath -Raw -Encoding UTF8
$VersionNode = $PropsXml.SelectSingleNode('//TravelCutProductVersion')
if ($null -eq $VersionNode -or [string]::IsNullOrWhiteSpace($VersionNode.InnerText)) {
    throw 'TravelCutProductVersion ausente em Directory.Build.props.'
}
$Version = $VersionNode.InnerText.Trim()

# ---------------------------------------------------------------------------
# Identificação Git
# ---------------------------------------------------------------------------
$GitCommit = 'unknown'
$GitShortSha = 'unknown'
$GitBranch = 'unknown'
$GitDirty = $false

try {
    $commitRaw = (& git rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($commitRaw)) {
        $GitCommit = $commitRaw.Trim()
    }
    $shortRaw = (& git rev-parse --short=8 HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($shortRaw)) {
        $GitShortSha = $shortRaw.Trim()
    }
    $branchRaw = (& git branch --show-current 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($branchRaw)) {
        $GitBranch = $branchRaw.Trim()
    }
    $statusRaw = (& git status --porcelain 2>$null)
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($statusRaw)) {
        $GitDirty = $true
    }
}
catch {
    # Git ausente ou repositorio nao inicializado
}

$PackageName = "TravelCutStudio-$Version-g$GitShortSha-$Runtime"
if ($GitDirty) { $PackageName = "$PackageName-dirty" }
if (-not $SelfContained) { $PackageName = "$PackageName-fdd" }
$StageDir = Join-Path $OutDir $PackageName
$AppDir = Join-Path $StageDir 'app'
$McpDir = Join-Path $StageDir 'mcp'

$FrontendDir = Join-Path $RepoRoot 'frontend'
$DesktopProj = Join-Path $RepoRoot 'src\TravelCut.Desktop\TravelCut.Desktop.csproj'
$McpProj = Join-Path $RepoRoot 'src\TravelCut.Mcp\TravelCut.Mcp.csproj'
$SolutionFile = Join-Path $RepoRoot 'TravelCutStudio.sln'
$TemplatePath = Join-Path $PSScriptRoot 'LEIA-ME-HOST.template.md'

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$LogDir = Join-Path $OutDir 'logs'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$LogFile = Join-Path $LogDir ('build-' + $StartedUtc.ToString('yyyyMMdd-HHmmss') + '.log')

# ---------------------------------------------------------------------------
# Funcoes auxiliares
# ---------------------------------------------------------------------------
function Write-Log {
    param([string]$Message)
    $line = '[' + [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ') + '] ' + $Message
    Write-Host $line
    [System.IO.File]::AppendAllText($LogFile, $line + [Environment]::NewLine, $Utf8NoBom)
}

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )
    Write-Log ('RUN  ' + $Label + ': ' + $FilePath + ' ' + ($Arguments -join ' '))
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $code = -1
    Push-Location -LiteralPath $WorkingDirectory
    try {
        & $FilePath @Arguments 2>&1 | ForEach-Object {
            $text = [string]$_
            Write-Host $text
            [System.IO.File]::AppendAllText($LogFile, $text + [Environment]::NewLine, $Utf8NoBom)
        }
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
        $ErrorActionPreference = $prevEap
    }
    $sw.Stop()
    $seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1)
    Write-Log ('EXIT ' + $Label + ': codigo=' + $code + ' duracao=' + $seconds + 's')
    if ($code -ne 0) {
        throw ('Falha em ' + $Label + ' (codigo ' + $code + '). Log: ' + $LogFile)
    }
    return [pscustomobject]@{ ExitCode = $code; Seconds = $seconds }
}

function Get-NativeText {
    param([string]$FilePath, [string[]]$Arguments)
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & $FilePath @Arguments 2>&1
    }
    finally {
        $ErrorActionPreference = $prevEap
    }
    return ((@($lines) | ForEach-Object { [string]$_ }) -join ' ').Trim()
}

function Find-Tool {
    param([string]$Name)
    $cmd = Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $cmd) { return $null }
    return $cmd.Source
}

function Copy-RepoFile {
    param([string]$RelPath, [string]$DestBase)
    $winRel = $RelPath -replace '/', '\'
    $src = Join-Path $RepoRoot $winRel
    if (-not (Test-Path -LiteralPath $src -PathType Leaf)) {
        throw ('Arquivo obrigatorio ausente no repositorio: ' + $RelPath)
    }
    $dst = Join-Path $DestBase $winRel
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
    Copy-Item -LiteralPath $src -Destination $dst -Force
    return $dst
}

function Get-RelativeName {
    param([string]$Base, [string]$FullPath)
    $rel = $FullPath.Substring($Base.Length).TrimStart('\')
    return ($rel -replace '\\', '/')
}

# ---------------------------------------------------------------------------
# Execucao
# ---------------------------------------------------------------------------
try {
    Write-Log ('Build de pacote de teste TravelCut Studio ' + $Version)
    Write-Log ('Repositorio: ' + $RepoRoot)
    Write-Log ('Saida: ' + $OutDir)
    Write-Log ('Configuracao=' + $Configuration + ' Runtime=' + $Runtime + ' SelfContained=' + $SelfContained + ' SkipTests=' + [bool]$SkipTests + ' Zip=' + [bool]$Zip)
    Write-Log ('Sistema: ' + [Environment]::OSVersion.VersionString + ' | PowerShell ' + $PSVersionTable.PSVersion.ToString())
    Write-Log ('Git: nao versionado em git (repositorio sem .git)')
    # Versão do produto injetada no bundle do frontend (vite.config.ts lê TRAVELCUT_VERSION).
    $env:TRAVELCUT_VERSION = $Version
    # Caminhos de origem neutros (/_/) em PDB/DLL: Directory.Build.props ativa ContinuousIntegrationBuild com TRAVELCUT_CI=1.
    $env:TRAVELCUT_CI = '1'
    Write-Log ('TRAVELCUT_CI=' + $env:TRAVELCUT_CI + ' TRAVELCUT_VERSION=' + $env:TRAVELCUT_VERSION)

    # Zip/hash de execução anterior não podem sobrar se esta falhar.
    foreach ($stale in @((Join-Path $OutDir ($PackageName + '.zip')), (Join-Path $OutDir ($PackageName + '.zip.sha256')))) {
        if (Test-Path -LiteralPath $stale) { Remove-Item -LiteralPath $stale -Force }
    }

    # (a) toolchain -------------------------------------------------------
    Write-Log '--- (a) toolchain ---'
    $DotnetExe = Find-Tool 'dotnet.exe'
    if (-not $DotnetExe) { throw 'dotnet.exe nao encontrado no PATH.' }
    $DotnetVersion = Get-NativeText $DotnetExe @('--version')
    if ($DotnetVersion -match '^(\d+)\.(\d+)') {
        $dotnetMajor = [int]$Matches[1]
    }
    else {
        throw ('Versao do dotnet nao reconhecida: ' + $DotnetVersion)
    }
    if ($dotnetMajor -lt 10) { throw ('SDK .NET 10 ou superior requerido; encontrado ' + $DotnetVersion) }
    Write-Log ('dotnet SDK ' + $DotnetVersion + ' (' + $DotnetExe + ')')

    $NodeExe = Find-Tool 'node.exe'
    if (-not $NodeExe) { throw 'node.exe nao encontrado no PATH.' }
    $NodeVersion = Get-NativeText $NodeExe @('--version')
    if ($NodeVersion -match '^v(\d+)\.') {
        $nodeMajor = [int]$Matches[1]
    }
    else {
        throw ('Versao do node nao reconhecida: ' + $NodeVersion)
    }
    if ($nodeMajor -lt 22) { throw ('Node.js 22 ou superior requerido (docs/14); encontrado ' + $NodeVersion) }
    Write-Log ('node ' + $NodeVersion + ' (' + $NodeExe + ')')

    $NpmCmd = Find-Tool 'npm.cmd'
    if (-not $NpmCmd) { throw 'npm.cmd nao encontrado no PATH.' }
    $NpmVersion = Get-NativeText $NpmCmd @('--version')
    Write-Log ('npm ' + $NpmVersion + ' (' + $NpmCmd + ')')

    # (b) frontend --------------------------------------------------------
    Write-Log '--- (b) frontend ---'
    $LockFile = Join-Path $FrontendDir 'package-lock.json'
    $HasLock = Test-Path -LiteralPath $LockFile -PathType Leaf
    if ($SkipFrontendInstall) {
        $NpmInstallMode = 'PULADO (-SkipFrontendInstall); node_modules pre-existente, nao reprodutivel'
        Write-Log 'AVISO: instalacao do frontend PULADA (-SkipFrontendInstall). Pacote marcado como nao reprodutivel.'
    }
    elseif ($HasLock) {
        $NpmInstallMode = 'npm ci'
        Invoke-Native 'npm ci' $NpmCmd @('ci', '--no-audit', '--no-fund') $FrontendDir | Out-Null
    }
    else {
        $NpmInstallMode = 'npm install (SEM package-lock.json)'
        Write-Log 'AVISO: frontend/package-lock.json ausente; usando npm install (build nao totalmente reprodutivel).'
        Invoke-Native 'npm install' $NpmCmd @('install', '--no-audit', '--no-fund') $FrontendDir | Out-Null
    }
    if ($SkipFrontendBuild) {
        Write-Log 'AVISO: npm run build PULADO (-SkipFrontendBuild). Usando frontend/dist existente; pacote nao reprodutivel.'
    }
    else {
        Invoke-Native 'npm run build' $NpmCmd @('run', 'build') $FrontendDir | Out-Null
    }

    $DistIndex = Join-Path $FrontendDir 'dist\index.html'
    if (-not (Test-Path -LiteralPath $DistIndex -PathType Leaf)) {
        throw 'frontend/dist/index.html nao foi gerado pelo npm run build.'
    }
    Write-Log ('frontend/dist/index.html gerado (' + (Get-Item -LiteralPath $DistIndex).Length + ' bytes)')
    if (-not $SkipFrontendBuild) {
        # A versão exibida na UI precisa ser a do pacote (sem cair em 'dev').
        $bundleHasVersion = Select-String -Path (Join-Path $FrontendDir 'dist\assets\*.js') -SimpleMatch -Pattern $Version -Quiet
        if (-not $bundleHasVersion) { throw ('Versao ' + $Version + ' nao encontrada no bundle do frontend.') }
        Write-Log ('Versao ' + $Version + ' presente no bundle do frontend.')
        # Checagem de tipos (o vite build nao checa tipos).
        Invoke-Native 'tsc --noEmit' $NpmCmd @('exec', '--', 'tsc', '--noEmit') $FrontendDir | Out-Null
    }

    # (c) testes ----------------------------------------------------------
    Write-Log '--- (c) testes ---'
    $TestInfo = [ordered]@{ executed = $false; reason = 'nao executado' }
    if ($SkipTests) {
        $TestInfo = [ordered]@{ executed = $false; reason = 'pulado por -SkipTests' }
        Write-Log 'Testes PULADOS (-SkipTests). Registrado no manifesto.'
    }
    else {
        $testRun = Invoke-Native 'dotnet test' $DotnetExe @('test', $SolutionFile, '-c', $Configuration, '--nologo') $RepoRoot
        $TestInfo = [ordered]@{
            executed    = $true
            command     = 'dotnet test TravelCutStudio.sln -c ' + $Configuration
            exitCode    = $testRun.ExitCode
            durationSec = $testRun.Seconds
        }
    }

    # (d) publish ---------------------------------------------------------
    Write-Log '--- (d) publish ---'
    if (Test-Path -LiteralPath $StageDir) {
        $resolvedStage = [System.IO.Path]::GetFullPath($StageDir)
        if (-not $resolvedStage.StartsWith($OutDir + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Pasta de pacote fora de OutputRoot; recusado.'
        }
        Remove-Item -LiteralPath $StageDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $StageDir | Out-Null

    $ScArg = 'false'
    if ($SelfContained) { $ScArg = 'true' }
    $PublishCommon = @(
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', $ScArg,
        '-p:PublishSingleFile=false',
        '-p:PublishReadyToRun=false',
        '-p:PublishTrimmed=false',
        '--nologo'
    )
    Invoke-Native 'publish Desktop' $DotnetExe (@('publish', $DesktopProj) + $PublishCommon + @('-o', $AppDir)) $RepoRoot | Out-Null
    Invoke-Native 'publish Mcp' $DotnetExe (@('publish', $McpProj) + $PublishCommon + @('-o', $McpDir)) $RepoRoot | Out-Null

    foreach ($req in @(
        (Join-Path $AppDir 'TravelCut.Desktop.exe'),
        (Join-Path $AppDir 'frontend\dist\index.html'),
        (Join-Path $AppDir 'workers\resolve_bridge.py'),
        (Join-Path $McpDir 'TravelCut.Mcp.exe')
    )) {
        if (-not (Test-Path -LiteralPath $req -PathType Leaf)) {
            throw ('Saida do publish incompleta, ausente: ' + $req.Substring($StageDir.Length))
        }
    }

    # (e) copias para o pacote -------------------------------------------
    Write-Log '--- (e) copias ---'
    # Workers: o whisper_worker.py nao e Content no csproj; o app o procura em <app>\workers\.
    foreach ($w in @('workers/resolve_bridge.py', 'workers/whisper_worker.py', 'workers/requirements.txt',
                     'workers/README.md', 'workers/hyperframes/README.md')) {
        $dst = Copy-RepoFile $w $AppDir
        Write-Log ('copiado: app/' + (Get-RelativeName $AppDir $dst))
    }

    $ToolsSrc = Join-Path $RepoRoot 'tools\host-validation'
    $ToolsDst = Join-Path $StageDir 'tools\host-validation'
    $toolFiles = @(Get-ChildItem -LiteralPath $ToolsSrc -Recurse -File)
    $toolCount = 0
    foreach ($tf in $toolFiles) {
        $rel = $tf.FullName.Substring($ToolsSrc.Length).TrimStart('\')
        if ($rel -match '(^|\\)evidencias(\\|$)') { continue }
        $dst = Join-Path $ToolsDst $rel
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dst) | Out-Null
        Copy-Item -LiteralPath $tf.FullName -Destination $dst -Force
        $toolCount = $toolCount + 1
    }
    Write-Log ('tools/host-validation copiados (sem evidencias): ' + $toolCount + ' arquivos')

    foreach ($d in @('docs/10-DAVINCI-VM-HOST.md', 'docs/14-BUILD-DEPLOY-HOST.md', 'docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md',
                     'docs/19-MCP-TRAVELCUT.md', 'docs/20-HYPERFRAMES.md', 'progresso/HOST_TESTS.md')) {
        Copy-RepoFile $d $StageDir | Out-Null
    }
    Write-Log 'docs 10/14/18/19/20 e progresso/HOST_TESTS.md copiados.'

    # (h) LEIA-ME ---------------------------------------------------------
    Write-Log '--- (h) LEIA-ME-HOST.md ---'
    $leia =[System.IO.File]::ReadAllText($TemplatePath, [System.Text.Encoding]::UTF8)
    $leia = $leia.Replace('{{VERSION}}', $Version)
    $leia = $leia.Replace('{{BUILD_UTC}}', $StartedUtc.ToString('yyyy-MM-ddTHH:mm:ssZ'))
    $leia = $leia.Replace('{{RUNTIME}}', $Runtime)
    $leia = $leia.Replace('{{SELF_CONTAINED}}', $(if ($SelfContained) { 'sim (runtime .NET incluido)' } else { 'nao (framework-dependent; exige .NET 10 Desktop Runtime)' }))
    $leia = $leia.Replace('{{PACKAGE}}', $PackageName)
    [System.IO.File]::WriteAllText((Join-Path $StageDir 'LEIA-ME-HOST.md'), $leia, $Utf8NoBom)

    # (f) proibicoes ------------------------------------------------------
    Write-Log '--- (f) varredura de conteudo proibido ---'
    $ForbiddenNamePattern = '\.(mp4|m4v|mov|mxf|braw|insv|r3d|mkv|avi|webm|mts|m2ts|wav|mp3|m4a|aac|flac|ogg|pt|pth|ckpt|safetensors|ggml|gguf|onnx|db|db-wal|db-shm|sqlite|sqlite3|tcpart|drp|pyc)$|\.db\.bak'
    $ForbiddenDirNames = @('node_modules', '.venv', 'venv', '__pycache__', '.cache', 'cache', 'evidencias', '.locks', '.git')
    $MaxBinBytes = 5MB
    $MaxFileBytes = 100MB
    $violations = New-Object System.Collections.Generic.List[string]

    Get-ChildItem -LiteralPath $StageDir -Recurse -Force | ForEach-Object {
        $rel = Get-RelativeName $StageDir $_.FullName
        if ($_.PSIsContainer) {
            if ($ForbiddenDirNames -contains $_.Name.ToLowerInvariant()) { $violations.Add('DIRETORIO PROIBIDO: ' + $rel) }
            return
        }
        if ($_.Name -match $ForbiddenNamePattern) { $violations.Add('EXTENSAO PROIBIDA: ' + $rel); return }
        if ($_.Extension -ieq '.bin' -and $_.Length -gt $MaxBinBytes) { $violations.Add('PESO GRANDE (.bin): ' + $rel); return }
        if ($_.Length -gt $MaxFileBytes) { $violations.Add('ARQUIVO > 100 MB: ' + $rel) }
    }
    if ($violations.Count -gt 0) {
        foreach ($v in $violations) { Write-Log $v }
        throw ('Pacote contem conteudo proibido (' + $violations.Count + ' item(ns)). Veja o log.')
    }
    Write-Log 'Varredura sem violacoes.'

    # (g) manifesto e hashes ----------------------------------------------
    Write-Log '--- (g) MANIFEST.json e SHA256SUMS.txt ---'
    $allFiles = Get-ChildItem -LiteralPath $StageDir -Recurse -File -Force |
        Where-Object { -not (($_.DirectoryName -eq $StageDir) -and (@('MANIFEST.json', 'SHA256SUMS.txt') -contains $_.Name)) } |
        Sort-Object FullName
    $fileEntries = New-Object System.Collections.Generic.List[object]
    $totalBytes = [int64]0
    foreach ($f in $allFiles) {
        $hash = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $totalBytes += $f.Length
        $fileEntries.Add([ordered]@{
            path   = (Get-RelativeName $StageDir $f.FullName)
            bytes  = $f.Length
            sha256 = $hash
        })
    }

    $reasons = New-Object System.Collections.Generic.List[string]
    if (-not $HasLock) { $reasons.Add('sem package-lock.json') }
    if ($SkipFrontendInstall) { $reasons.Add('-SkipFrontendInstall') }
    if ($SkipFrontendBuild) { $reasons.Add('-SkipFrontendBuild') }
    if ($SkipTests) { $reasons.Add('-SkipTests') }
    if (-not $SelfContained) { $reasons.Add('framework-dependent') }
    # Entradas fixadas (lockfile, testes, build determinístico com caminhos neutros) não provam bit-a-bit:
    # sem git não há hash de fonte confiável nem comparação entre dois builds. Por isso o campo é 'unverified'.
    $isReproducible = if ($reasons.Count -eq 0) { 'unverified (entradas fixadas; build deterministico; nao comparado entre dois builds)' } else { $false }

    $manifest = [ordered]@{
        product        = 'TravelCut Studio'
        productVersion = $Version
        version        = $Version
        package        = $PackageName
        gitCommit      = $GitCommit
        gitShortSha    = $GitShortSha
        gitBranch      = $GitBranch
        gitDirty       = $GitDirty
        buildUtc       = $StartedUtc.ToString('yyyy-MM-ddTHH:mm:ssZ')
        configuration  = $Configuration
        runtimeIdentifier = $Runtime
        runtime        = $Runtime
        selfContained  = [bool]$SelfContained
        reproducible   = $isReproducible
        nonReproducibleReasons = $reasons.ToArray()
        targets        = [ordered]@{ app = 'net10.0-windows (WPF + WebView2)'; mcp = 'net10.0 (console stdio)' }
        commit         = $GitCommit
        frontendVersion = $Version
        dotnetVersion  = $DotnetVersion
        testsExecuted  = if ($TestInfo -and $TestInfo.total) { $TestInfo.total } else { 0 }
        testsPassed    = if ($TestInfo -and $TestInfo.passed) { $TestInfo.passed } else { 0 }
        toolchain      = [ordered]@{
            dotnetSdk = $DotnetVersion
            node      = $NodeVersion
            npm       = $NpmVersion
            os        = [Environment]::OSVersion.VersionString
            powershell = $PSVersionTable.PSVersion.ToString()
        }
        frontend       = [ordered]@{
            install     = $NpmInstallMode
            build       = 'npm run build (vite build)'
            indexHtml   = 'app/frontend/dist/index.html'
        }
        publish        = [ordered]@{
            singleFile  = $false
            readyToRun  = $false
            trimmed     = $false
            reason      = 'WPF + WebView2Loader nativo + frontend/workers em disco ao lado do exe; single-file adiciona extracao temporaria e risco de falha no host sem ganho para homologacao.'
        }
        tests          = $TestInfo
        signing        = 'nao assinado'
        totals         = [ordered]@{ fileCount = $fileEntries.Count; bytes = $totalBytes }
        files          = $fileEntries.ToArray()
    }
    $manifestPath = Join-Path $StageDir 'MANIFEST.json'
    [System.IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine), $Utf8NoBom)

    $sumLines = New-Object System.Collections.Generic.List[string]
    $sumFiles = Get-ChildItem -LiteralPath $StageDir -Recurse -File -Force | Sort-Object FullName
    foreach ($f in $sumFiles) {
        if ($f.DirectoryName -eq $StageDir -and $f.Name -eq 'SHA256SUMS.txt') { continue }
        $h = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $sumLines.Add($h + '  ' + (Get-RelativeName $StageDir $f.FullName))
    }
    [System.IO.File]::WriteAllText((Join-Path $StageDir 'SHA256SUMS.txt'), (($sumLines -join "`n") + "`n"), $Utf8NoBom)
    Write-Log ('MANIFEST.json e SHA256SUMS.txt gerados: ' + $fileEntries.Count + ' arquivos no manifesto, ' + $sumLines.Count + ' linhas em SHA256SUMS.')

    $sizeMb = [math]::Round(((Get-ChildItem -LiteralPath $StageDir -Recurse -File | Measure-Object -Property Length -Sum).Sum) / 1MB, 1)
    Write-Log ('Tamanho do pacote: ' + $sizeMb + ' MB (' + $StageDir + ')')

    # (i) zip -------------------------------------------------------------
    if ($Zip) {
        Write-Log '--- (i) zip ---'
        $zipPath = Join-Path $OutDir ($PackageName + '.zip')
        if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
        Compress-Archive -LiteralPath $StageDir -DestinationPath $zipPath -CompressionLevel Optimal
        $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        [System.IO.File]::WriteAllText(($zipPath + '.sha256'), ($zipHash + '  ' + ($PackageName + '.zip') + "`n"), $Utf8NoBom)
        $zipMb = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)
        Write-Log ('ZIP: ' + $zipPath + ' (' + $zipMb + ' MB) sha256=' + $zipHash)
    }

    $elapsed = [math]::Round(([DateTime]::UtcNow - $StartedUtc).TotalSeconds, 1)
    Write-Log ('CONCLUIDO em ' + $elapsed + 's. Pacote: ' + $StageDir)
    exit 0
}
catch {
    Write-Log ('ERRO: ' + $_.Exception.Message)
    Write-Host ('Build falhou. Log completo: ' + $LogFile) -ForegroundColor Red
    exit 1
}
