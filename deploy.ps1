#Requires -Version 5.1
<#
.SYNOPSIS
    Gera o pacote de distribuição do TravelCut Studio em dist\.

.DESCRIPTION
    Atalho na raiz do projeto para scripts\release\Build-Release.ps1, que faz todo o trabalho:
    build do frontend, testes .NET, publish de Desktop e MCP (win-x64), varredura de conteúdo proibido,
    MANIFEST.json, SHA256SUMS.txt, LEIA-ME-HOST.md e, por padrão, o .zip com hash.
    Nada é enviado para rede, Z: ou GitHub.

.PARAMETER SemTestes
    Pula dotnet test (o manifesto registra que os testes não rodaram).

.PARAMETER SemZip
    Gera só a pasta do pacote, sem o .zip.

.PARAMETER FrameworkDependent
    Pacote sem o runtime .NET embutido (menor; exige .NET 10 instalado no computador de destino).

.EXAMPLE
    .\deploy.ps1
.EXAMPLE
    .\deploy.ps1 -SemTestes -SemZip
#>
[CmdletBinding()]
param(
    [switch]$SemTestes,
    [switch]$SemZip,
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$buildScript = Join-Path $PSScriptRoot 'scripts\release\Build-Release.ps1'
if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
    Write-Error "Script de build não encontrado: $buildScript"
    exit 1
}

$arguments = @{ OutputRoot = 'dist'; SelfContained = (-not $FrameworkDependent) }
if (-not $SemZip) { $arguments.Zip = $true }
if ($SemTestes) { $arguments.SkipTests = $true }

Write-Host 'TravelCut Studio - gerando pacote de distribuição em dist\' -ForegroundColor Cyan
& $buildScript @arguments
$exitCode = $LASTEXITCODE
if ($null -eq $exitCode) { $exitCode = 0 }

if ($exitCode -ne 0) {
    Write-Host "Falha ao gerar o pacote (código $exitCode). Veja o log mais recente em dist\logs\." -ForegroundColor Red
    exit $exitCode
}

# Resumo: pacote mais recente e hash do zip (quando gerado).
$dist = Join-Path $PSScriptRoot 'dist'
$package = Get-ChildItem -LiteralPath $dist -Directory -Filter 'TravelCutStudio-*' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($package) {
    Write-Host ''
    Write-Host "Pacote: $($package.FullName)" -ForegroundColor Green
    $zipHash = "$($package.FullName).zip.sha256"
    if (Test-Path -LiteralPath $zipHash) {
        Write-Host "Zip:    $($package.FullName).zip"
        Write-Host "SHA256: $((Get-Content -LiteralPath $zipHash -Raw).Trim())"
    }
}
exit 0
