<#
.SYNOPSIS
  Envia UM pedido ao bridge workers/resolve_bridge.py e grava pedido, resposta e stderr em EvidenceDir.

.DESCRIPTION
  - Protocolo "resolve-bridge/1": stdin = uma linha JSON; stdout = uma linha JSON (a ultima linha JSON e' usada).
  - Python: -PythonCommand, senao variavel TRAVELCUT_PYTHON, senao "py -3", senao "python".
  - Timeout: encerra a arvore do processo com taskkill /T /F (nao encerra o Resolve).
  - Nada de UI automation: apenas o bridge (API de scripting).
  - Evidencias contem caminhos locais: nao copie para repositorios ou relatorios publicos sem revisao.

  Retorna um objeto com: Step, Command, ProcessStatus (Completed|TimedOut|NotStarted), ExitCode,
  Response (objeto ou $null), ResponsePath, RequestPath, StderrPath, DurationMs.

  Estado: escrito e revisado por leitura; NAO executado na VM (sem Python e sem Resolve).

.EXAMPLE
  .\Invoke-ResolveBridgeStep.ps1 -Command probe -StepName probe -EvidenceDir .\evidencias\20261102T120000Z
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Command,
    [Parameter(Mandatory = $true)][string]$StepName,
    [Parameter(Mandatory = $true)][string]$EvidenceDir,
    [hashtable]$Payload = @{},
    [int]$TimeoutSec = 120,
    [string]$BridgeScriptPath = '',
    [string]$PythonCommand = ''
)

$ErrorActionPreference = 'Stop'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

if (-not $BridgeScriptPath) {
    $BridgeScriptPath = Join-Path $PSScriptRoot '..\..\..\workers\resolve_bridge.py'
}
$BridgeScriptPath = [System.IO.Path]::GetFullPath($BridgeScriptPath)
$EvidenceDir = [System.IO.Path]::GetFullPath($EvidenceDir)
New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null

$requestPath = Join-Path $EvidenceDir "$StepName.request.json"
$responsePath = Join-Path $EvidenceDir "$StepName.response.json"
$stderrPath = Join-Path $EvidenceDir "$StepName.stderr.txt"

function New-StepRecord([string]$ProcessStatus, $ExitCode, $Response, [string]$Raw, [string]$Stderr, [long]$DurationMs) {
    [System.IO.File]::WriteAllText($responsePath, $Raw, $Utf8NoBom)
    [System.IO.File]::WriteAllText($stderrPath, $Stderr, $Utf8NoBom)
    return [pscustomobject]@{
        Step          = $StepName
        Command       = $Command
        ProcessStatus = $ProcessStatus
        ExitCode      = $ExitCode
        Response      = $Response
        RequestPath   = $requestPath
        ResponsePath  = $responsePath
        StderrPath    = $stderrPath
        DurationMs    = $DurationMs
    }
}

function Resolve-PythonInvocation([string]$Explicit) {
    if ($Explicit) { return @{ File = $Explicit; Prefix = @() } }
    if ($env:TRAVELCUT_PYTHON) { return @{ File = $env:TRAVELCUT_PYTHON; Prefix = @() } }
    if (Get-Command 'py' -ErrorAction SilentlyContinue) { return @{ File = 'py'; Prefix = @('-3') } }
    if (Get-Command 'python' -ErrorAction SilentlyContinue) { return @{ File = 'python'; Prefix = @() } }
    return $null
}

# Pedido: schemaVersion + command + campos do payload no nivel superior (contrato do bridge).
$request = [ordered]@{ schemaVersion = 'resolve-bridge/1'; command = $Command }
foreach ($key in $Payload.Keys) {
    $request[$key] = $Payload[$key]
}
$requestJson = $request | ConvertTo-Json -Depth 8 -Compress
[System.IO.File]::WriteAllText($requestPath, $requestJson, $Utf8NoBom)

if (-not (Test-Path -LiteralPath $BridgeScriptPath)) {
    return New-StepRecord 'NotStarted' $null $null '' 'Script do bridge nao encontrado.' 0
}

$python = Resolve-PythonInvocation $PythonCommand
if ($null -eq $python) {
    return New-StepRecord 'NotStarted' $null $null '' 'Python 3 nao encontrado (defina TRAVELCUT_PYTHON).' 0
}

$argList = @()
$argList += $python.Prefix
$argList += $BridgeScriptPath
$argString = ($argList | ForEach-Object { '"' + ($_ -replace '"', '\"') + '"' }) -join ' '

$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = $python.File
$psi.Arguments = $argString
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.EnvironmentVariables['PYTHONUTF8'] = '1'
$psi.EnvironmentVariables['PYTHONIOENCODING'] = 'utf-8'

$clock = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $proc = [System.Diagnostics.Process]::Start($psi)
}
catch {
    return New-StepRecord 'NotStarted' $null $null '' 'Falha ao iniciar Python.' $clock.ElapsedMilliseconds
}

$stdoutTask = $proc.StandardOutput.ReadToEndAsync()
$stderrTask = $proc.StandardError.ReadToEndAsync()

try {
    $bytes = $Utf8NoBom.GetBytes($requestJson + "`n")
    $proc.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
    $proc.StandardInput.BaseStream.Flush()
}
catch [System.IO.IOException] {
    # Processo encerrou antes de ler o stdin; a saida decide o resultado.
}
finally {
    try { $proc.StandardInput.Close() } catch { }
}

$exited = $proc.WaitForExit($TimeoutSec * 1000)
if (-not $exited) {
    & taskkill.exe /PID $proc.Id /T /F | Out-Null
    $proc.WaitForExit(5000) | Out-Null
    $processStatus = 'TimedOut'
    $exitCode = $null
}
else {
    $processStatus = 'Completed'
    $exitCode = $proc.ExitCode
}

[void]$stdoutTask.Wait(10000)
[void]$stderrTask.Wait(10000)
$stdout = ''
$stderr = ''
if ($stdoutTask.IsCompleted) { $stdout = $stdoutTask.Result }
if ($stderrTask.IsCompleted) { $stderr = $stderrTask.Result }
$clock.Stop()

$response = $null
$rawResponse = $stdout
$jsonLines = @($stdout -split "`r?`n" | Where-Object { $_.Trim().StartsWith('{') })
if ($jsonLines.Count -gt 0) {
    $rawResponse = $jsonLines[-1]
    try {
        $response = $rawResponse | ConvertFrom-Json
    }
    catch {
        $response = $null
    }
}

return New-StepRecord $processStatus $exitCode $response $rawResponse $stderr $clock.ElapsedMilliseconds
