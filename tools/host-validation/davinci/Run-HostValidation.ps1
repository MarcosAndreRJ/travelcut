<#
.SYNOPSIS
  Roteiro automatizado de homologacao do bridge DaVinci no host (probe -> apply -> query -> export -> render
  + testes negativos). Cada etapa registra PASS, FAIL ou NOT_RUN em evidencias/<utc>/log.txt.

.DESCRIPTION
  Regras de seguranca:
  - Somente projetos com prefixo TravelCut_ (validado aqui e no bridge). Nomes sem prefixo sao enviados
    apenas em testes negativos e recusados antes de qualquer acesso ao Resolve.
  - Nunca abre/altera projeto pessoal: cada aplicacao cria projeto NOVO de homologacao.
  - Se ja houver projeto aberto no Resolve, as etapas que trocam o projeto exigem -ConfirmProjectSwitch.
  - Nenhum passo fecha o Resolve; o teste "Resolve fechado" so e executado se o Resolve ja estiver fechado.
  - Nao faz UI automation.

  Estado: escrito e revisado por leitura; NAO executado na VM (sem Python/DaVinci). Execucao: host_pending.
  Resultado de host so vale apos execucao humana no host com este log anexado (ver docs/18).

.EXAMPLE
  .\Run-HostValidation.ps1 -MediaManifestPath "$env:TEMP\TravelCutHarness\media\synthetic-media.json"

.EXAMPLE
  .\Run-HostValidation.ps1 -MediaManifestPath ... -EndFrameInclusive:$false -ConfirmProjectSwitch
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$MediaManifestPath,
    [string]$AssemblyPath = '',
    [string]$ProjectName = '',
    [string]$EvidenceRoot = (Join-Path $PSScriptRoot 'evidencias'),
    [string]$BridgeScriptPath = '',
    [string]$PythonCommand = '',
    [bool]$EndFrameInclusive = $true,
    [switch]$ConfirmProjectSwitch,
    [switch]$SkipNegativeTests
)

$ErrorActionPreference = 'Stop'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$invoke = Join-Path $PSScriptRoot 'Invoke-ResolveBridgeStep.ps1'
$newAssembly = Join-Path $PSScriptRoot 'New-SyntheticAssembly.ps1'

if (-not $BridgeScriptPath) {
    $BridgeScriptPath = Join-Path $PSScriptRoot '..\..\..\workers\resolve_bridge.py'
}
$BridgeScriptPath = [System.IO.Path]::GetFullPath($BridgeScriptPath)

$utc = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$run = Join-Path ([System.IO.Path]::GetFullPath($EvidenceRoot)) $utc
New-Item -ItemType Directory -Force -Path (Join-Path $run 'export') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $run 'render') | Out-Null
$logPath = Join-Path $run 'log.txt'
$script:Results = New-Object System.Collections.ArrayList

function Add-HarnessResult {
    param([string]$Step, [ValidateSet('PASS', 'FAIL', 'NOT_RUN')][string]$Status, [string]$Detail)
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    $line = "[$stamp] $Step | $Status | $Detail"
    Add-Content -LiteralPath $logPath -Value $line -Encoding UTF8
    Write-Host $line
    [void]$script:Results.Add([pscustomobject]@{ step = $Step; status = $Status; detail = $Detail })
}

function Assert-TravelCutName {
    param([string]$Name)
    if ($Name -notmatch '^TravelCut_[A-Za-z0-9_\-]{1,140}$') {
        throw "Nome '$Name' sem prefixo TravelCut_: o harness nao usa esse projeto."
    }
}

function Invoke-Bridge {
    param([string]$StepName, [string]$Command, [hashtable]$Payload = @{}, [int]$TimeoutSec = 120)
    return & $invoke -Command $Command -StepName $StepName -EvidenceDir $run -Payload $Payload `
        -TimeoutSec $TimeoutSec -BridgeScriptPath $BridgeScriptPath -PythonCommand $PythonCommand
}

function Get-ErrorText($record) {
    if ($null -eq $record) { return 'sem resposta' }
    if ($record.ProcessStatus -ne 'Completed') { return "processo $($record.ProcessStatus)" }
    if ($null -eq $record.Response) { return 'resposta nao-JSON' }
    $code = $record.Response.errorCode
    $msg = $record.Response.errorMessage
    return "errorCode=$code; $msg"
}

function Format-Steps($response) {
    if ($null -eq $response -or $null -eq $response.steps) { return '(sem etapas)' }
    return (@($response.steps) | ForEach-Object { "$($_.operation)=$($_.status)" }) -join ', '
}

# ---------------------------------------------------------------------------
Add-HarnessResult 'precheck_environment' 'PASS' ("ps=$($PSVersionTable.PSVersion); run=$utc; endFrameInclusive=$EndFrameInclusive")

if (-not (Test-Path -LiteralPath $MediaManifestPath)) {
    Add-HarnessResult 'precheck_media_manifest' 'FAIL' 'synthetic-media.json nao encontrado. Rode New-SyntheticMedia.ps1.'
    exit 1
}
$mediaDoc = Get-Content -LiteralPath $MediaManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
Add-HarnessResult 'precheck_media_manifest' 'PASS' ("midias=" + @($mediaDoc.media).Count)

# Raiz permitida pelo bridge (allowedOutputRoot) = <projeto>\3-Trabalho\editor. O bridge recusa saida fora dela.
# - Sem -AssemblyPath: projeto temporario de homologacao em <run>\proj; midias sinteticas copiadas para <proj>\1-Bruto.
# - Com -AssemblyPath: a raiz e derivada do proprio caminho (...\3-Trabalho\editor\...); midias devem estar em 1-Bruto/2-Assets.
$allowedRoot = $null
if ($AssemblyPath) {
    if (-not (Test-Path -LiteralPath $AssemblyPath)) { throw 'AssemblyPath informado nao existe.' }
    $fullAssemblyPath = [System.IO.Path]::GetFullPath($AssemblyPath)
    $markerIndex = $fullAssemblyPath.LastIndexOf('\3-Trabalho\editor\', [System.StringComparison]::OrdinalIgnoreCase)
    if ($markerIndex -lt 0) { throw 'AssemblyPath deve estar em <projeto>\3-Trabalho\editor\...' }
    $allowedRoot = $fullAssemblyPath.Substring(0, $markerIndex) + '\3-Trabalho\editor'
    $assemblyDoc = Get-Content -LiteralPath $AssemblyPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $ProjectName = [string]$assemblyDoc.editorProjectName
    Copy-Item -LiteralPath $AssemblyPath -Destination (Join-Path $run 'assembly.json')
}
else {
    $projRoot = Join-Path $run 'proj'
    $allowedRoot = Join-Path $projRoot '3-Trabalho\editor'
    $brutoDir = Join-Path $projRoot '1-Bruto'
    New-Item -ItemType Directory -Force -Path $allowedRoot, $brutoDir | Out-Null
    foreach ($media in @($mediaDoc.media)) {
        $dest = Join-Path $brutoDir ([string]$media.fileName)
        Copy-Item -LiteralPath ([string]$media.absolutePath) -Destination $dest -Force
        $media.absolutePath = $dest
    }
    $projMediaPath = Join-Path $run 'media-in-project.json'
    [System.IO.File]::WriteAllText($projMediaPath, ($mediaDoc | ConvertTo-Json -Depth 8), $Utf8NoBom)
    if (-not $ProjectName) { $ProjectName = 'TravelCut_Harness_' + $utc.TrimEnd('Z') }
    & $newAssembly -MediaManifestPath $projMediaPath -OutputPath (Join-Path $run 'assembly.json') -ProjectName $ProjectName | Out-Null
}
# Artefatos de apply/export/render ficam sob a raiz permitida (pasta de trabalho, nunca 1-Bruto/2-Assets).
$artifactDir = Join-Path $allowedRoot ('harness-' + $utc)
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$assemblyFile = Join-Path $run 'assembly.json'
$assembly = Get-Content -LiteralPath $assemblyFile -Raw -Encoding UTF8 | ConvertFrom-Json
Assert-TravelCutName $ProjectName
$timelineName = [string]$assembly.timelineName
$expectedClips = @($assembly.clips | ForEach-Object { "$($_.recordFrame)/$($_.durationFrames)" } | Sort-Object)

if ($assembly.schemaVersion -eq 'editor-assembly/1' -and @($assembly.clips).Count -gt 0) {
    Add-HarnessResult 'precheck_assembly' 'PASS' ("projeto=$ProjectName; clipes=" + @($assembly.clips).Count)
}
else {
    Add-HarnessResult 'precheck_assembly' 'FAIL' 'Manifesto de montagem invalido.'
    exit 1
}

$resolveRunning = [bool](Get-Process -Name 'Resolve' -ErrorAction SilentlyContinue)
Add-HarnessResult 'precheck_resolve_process' ($(if ($resolveRunning) { 'PASS' } else { 'FAIL' })) `
    ($(if ($resolveRunning) { 'Resolve em execucao.' } else { 'Resolve nao esta em execucao: passos com Resolve ficarao FAIL/NOT_RUN.' }))

# ---------------------------------------------------------------------------
# 1. probe (somente leitura)
$probe = Invoke-Bridge -StepName 'probe' -Command 'probe' -TimeoutSec 60
$probeOk = $false
$switchBlocked = $false
$openProject = $null
if ($probe.ProcessStatus -eq 'Completed' -and $probe.Response -and $probe.Response.ok -eq $true -and
    @('READY', 'READY_PARTIAL') -contains $probe.Response.status) {
    $probeOk = $true
    $openProject = $probe.Response.openProjectName
    $switchBlocked = ($null -ne $openProject) -and (-not $ConfirmProjectSwitch)
    Add-HarnessResult 'probe' 'PASS' ("status=$($probe.Response.status); produto=$($probe.Response.productName); versao=$($probe.Response.version); python=$($probe.Response.pythonVersion); $($probe.Response.methodsNote)")
    if ($null -ne $openProject) {
        Add-HarnessResult 'probe_open_project' 'PASS' "Projeto aberto no Resolve: '$openProject' (escrita trocaria o projeto atual; exige -ConfirmProjectSwitch)."
    }
}
else {
    Add-HarnessResult 'probe' 'FAIL' (Get-ErrorText $probe)
}

# M7: a edicao precisa ser DaVinci Resolve STUDIO (o bridge e validado para Studio).
if ($probeOk) {
    $productName = [string]$probe.Response.productName
    if ($productName -match 'Studio') {
        Add-HarnessResult 'probe_product_studio' 'PASS' "productName=$productName"
    }
    else {
        Add-HarnessResult 'probe_product_studio' 'FAIL' "productName='$productName' nao contem 'Studio'; homologacao exige DaVinci Resolve Studio."
    }
}

$methodsMissing = @()
$methodsUnverified = @()
if ($probeOk -and $probe.Response.methods) {
    foreach ($prop in $probe.Response.methods.PSObject.Properties) {
        if ($prop.Value -eq $false) { $methodsMissing += $prop.Name }
    }
}

# ---------------------------------------------------------------------------
# 2. apply_assembly (cria projeto NOVO, timeline, aplica 4 cortes, consulta, salva, snapshot)
$applyResponse = $null
$snapshotApply = Join-Path $artifactDir 'snapshot-apply.drp'
$applyPayload = @{
    manifestPath       = $assemblyFile
    allowedOutputRoot  = $allowedRoot
    endFrameInclusive  = $EndFrameInclusive
    snapshotPath       = $snapshotApply
}
if (-not $probeOk) {
    Add-HarnessResult 'apply_assembly' 'NOT_RUN' 'Probe falhou; corrija o ambiente antes.'
}
elseif ($switchBlocked) {
    Add-HarnessResult 'apply_assembly' 'NOT_RUN' "Projeto '$openProject' aberto; repita com -ConfirmProjectSwitch depois de confirmar a troca."
}
else {
    $apply = Invoke-Bridge -StepName 'apply_assembly' -Command 'apply_assembly' -Payload $applyPayload -TimeoutSec 900
    $applyResponse = $apply.Response
    if ($apply.ProcessStatus -eq 'Completed' -and $applyResponse -and $applyResponse.status -eq 'READY_REAL') {
        Add-HarnessResult 'apply_assembly' 'PASS' ("READY_REAL; etapas: " + (Format-Steps $applyResponse) + "; versao=$($applyResponse.editorVersion)")
    }
    elseif ($applyResponse -and $applyResponse.status -eq 'PARTIAL_REAL') {
        Add-HarnessResult 'apply_assembly' 'FAIL' ("PARTIAL_REAL (rejeicoes=" + @($applyResponse.rejected).Count +
            ", divergencias=" + @($applyResponse.clipMappings | Where-Object { $_.matches -eq $false }).Count +
            ", in-point nao verificado=" + @($applyResponse.clipMappings | Where-Object { $null -eq $_.matches }).Count +
            "); etapas: " + (Format-Steps $applyResponse))
    }
    else {
        Add-HarnessResult 'apply_assembly' 'FAIL' ((Get-ErrorText $apply) + '; etapas: ' + (Format-Steps $applyResponse))
    }
}

# A3: in-point observado (leftOffset do item) vs sourceStartFrame do manifesto; tolerancia 0.
if ($applyResponse -and $applyResponse.clipMappings) {
    $clipById = @{}
    foreach ($c in @($assembly.clips)) { $clipById[[string]$c.clipId] = $c }
    $inPointFails = @()
    $inPointChecked = 0
    $inPointUnobserved = 0
    foreach ($m in @($applyResponse.clipMappings)) {
        $c = $clipById[[string]$m.clipId]
        if ($null -eq $c) { $inPointFails += "clipe desconhecido $($m.clipId)"; continue }
        if ($null -eq $m.leftOffsetFrames) { $inPointUnobserved++; continue }
        $inPointChecked++
        if ([long]$m.leftOffsetFrames -ne [long]$c.sourceStartFrame) {
            $inPointFails += "recordFrame=$($c.recordFrame): leftOffset=$($m.leftOffsetFrames) sourceStartFrame=$($c.sourceStartFrame)"
        }
    }
    if ($inPointFails.Count -gt 0) {
        Add-HarnessResult 'apply_in_point' 'FAIL' ('In-point divergente: ' + ($inPointFails -join '; '))
    }
    elseif ($inPointUnobserved -gt 0) {
        # docs/18 §4: in-point nao observado NAO e PASS parcial (nao conta como conferido). NOT_RUN com a contagem.
        $totalMappings = @($applyResponse.clipMappings).Count
        Add-HarnessResult 'apply_in_point' 'NOT_RUN' "In-point nao observado em $inPointUnobserved de $totalMappings clipe(s) (API nao expos leftOffset); nao conta como conferido."
    }
    elseif ($inPointChecked -eq 0) {
        Add-HarnessResult 'apply_in_point' 'NOT_RUN' "Nenhum clipe mapeado para verificar in-point."
    }
    else {
        Add-HarnessResult 'apply_in_point' 'PASS' "In-point (leftOffset) = sourceStartFrame em $inPointChecked clipe(s), tolerancia 0."
    }
}

# ---------------------------------------------------------------------------
# 3. query_timeline (leitura do projeto criado)
if ($applyResponse -and $applyResponse.status -in @('READY_REAL', 'PARTIAL_REAL')) {
    $query = Invoke-Bridge -StepName 'query_timeline' -Command 'query_timeline' -Payload @{
        projectName  = $ProjectName
        timelineName = $timelineName
    } -TimeoutSec 120
    if ($query.ProcessStatus -eq 'Completed' -and $query.Response -and $query.Response.ok -eq $true) {
        $observed = @($query.Response.items | ForEach-Object { "$($_.recordFrame)/$($_.durationFrames)" } | Sort-Object)
        $same = ($observed.Count -eq $expectedClips.Count) -and
                ((@(Compare-Object -ReferenceObject $expectedClips -DifferenceObject $observed)).Count -eq 0)
        $detail = "esperados=$($expectedClips.Count) observados=$($observed.Count); inicioTimeline=$($query.Response.timelineStartFrame); recordFrame/duracao esperados: " + ($expectedClips -join ' ') + '; observados: ' + ($observed -join ' ')
        Add-HarnessResult 'query_timeline' ($(if ($same) { 'PASS' } else { 'FAIL' })) $detail
    }
    else {
        Add-HarnessResult 'query_timeline' 'FAIL' (Get-ErrorText $query)
    }
}
else {
    Add-HarnessResult 'query_timeline' 'NOT_RUN' 'apply_assembly nao concluiu (READY_REAL/PARTIAL_REAL).'
}

# ---------------------------------------------------------------------------
# 4. export_project (.drp do projeto novo) e 5. render_preview (MP4 de previa)
if ($applyResponse -and $applyResponse.status -in @('READY_REAL', 'PARTIAL_REAL') -and -not $switchBlocked) {
    $drp = Join-Path $artifactDir 'project-export.drp'
    $exp = Invoke-Bridge -StepName 'export_project' -Command 'export_project' -Payload @{
        projectName       = $ProjectName
        outputPath        = $drp
        allowedOutputRoot = $allowedRoot
    } -TimeoutSec 600
    if ($exp.ProcessStatus -eq 'Completed' -and $exp.Response -and $exp.Response.ok -eq $true -and
        (Test-Path -LiteralPath $drp) -and ((Get-Item -LiteralPath $drp).Length -gt 0)) {
        Add-HarnessResult 'export_project' 'PASS' ("arquivo gerado, bytes=" + (Get-Item -LiteralPath $drp).Length)
    }
    else {
        Add-HarnessResult 'export_project' 'FAIL' (Get-ErrorText $exp)
    }

    $rd = Invoke-Bridge -StepName 'render_preview' -Command 'render_preview' -Payload @{
        projectName       = $ProjectName
        timelineName      = $timelineName
        outputDir         = (Join-Path $artifactDir 'render')
        allowedOutputRoot = $allowedRoot
        timeoutSec        = 900
    } -TimeoutSec 1000
    $renderedFile = $null
    if ($rd.Response -and $rd.Response.outputAbsolutePath) { $renderedFile = [string]$rd.Response.outputAbsolutePath }
    if ($rd.ProcessStatus -eq 'Completed' -and $rd.Response -and $rd.Response.ok -eq $true -and $renderedFile -and (Test-Path -LiteralPath $renderedFile)) {
        Add-HarnessResult 'render_preview' 'PASS' ("MP4 gerado; tempo medido do bridge=$($rd.DurationMs) ms (inclui espera do render)")
    }
    else {
        Add-HarnessResult 'render_preview' 'FAIL' (Get-ErrorText $rd)
    }
}
else {
    Add-HarnessResult 'export_project' 'NOT_RUN' 'Projeto novo nao foi criado nesta execucao.'
    Add-HarnessResult 'render_preview' 'NOT_RUN' 'Projeto novo nao foi criado nesta execucao.'
}

# ---------------------------------------------------------------------------
# Testes negativos
if (-not $SkipNegativeTests) {

    # Numeracao N1..N7 = docs/18 secao 4 (mesmos nomes de passo).
    # N1. Nome sem prefixo: recusado antes de qualquer acesso ao Resolve (nome inventado, nao e projeto real).
    $neg1 = Invoke-Bridge -StepName 'neg_name_without_prefix' -Command 'query_timeline' -Payload @{
        projectName  = 'Harness_SemPrefixo_Teste'
        timelineName = 'x'
    } -TimeoutSec 30
    if ($neg1.Response -and $neg1.Response.errorCode -eq 'PERMISSION_DENIED') {
        Add-HarnessResult 'neg_name_without_prefix' 'PASS' 'query_timeline recusado: PERMISSION_DENIED (sem acesso ao Resolve).'
    }
    else {
        Add-HarnessResult 'neg_name_without_prefix' 'FAIL' (Get-ErrorText $neg1)
    }

    $neg2 = Invoke-Bridge -StepName 'neg_export_without_prefix' -Command 'export_project' -Payload @{
        projectName = 'Harness_SemPrefixo_Teste'
        outputPath  = (Join-Path $run 'export\neg-sem-prefixo.drp')
    } -TimeoutSec 30
    if ($neg2.Response -and $neg2.Response.errorCode -eq 'PERMISSION_DENIED' -and -not (Test-Path -LiteralPath (Join-Path $run 'export\neg-sem-prefixo.drp'))) {
        Add-HarnessResult 'neg_export_without_prefix' 'PASS' 'export_project recusado: PERMISSION_DENIED; nenhum arquivo criado.'
    }
    else {
        Add-HarnessResult 'neg_export_without_prefix' 'FAIL' (Get-ErrorText $neg2)
    }

    # N3. Projeto repetido: mesma montagem, mesmo nome -> CreateProject deve recusar.
    if ($probeOk -and -not $switchBlocked -and $applyResponse -and $applyResponse.status -in @('READY_REAL', 'PARTIAL_REAL')) {
        $neg3 = Invoke-Bridge -StepName 'neg_project_exists' -Command 'apply_assembly' -Payload @{
            manifestPath      = $assemblyFile
            allowedOutputRoot = $allowedRoot
            endFrameInclusive = $EndFrameInclusive
        } -TimeoutSec 300
        if ($neg3.Response -and $neg3.Response.errorCode -eq 'PROJECT_EXISTS') {
            Add-HarnessResult 'neg_project_exists' 'PASS' 'Projeto repetido recusado: PROJECT_EXISTS; nenhum projeto existente foi aberto.'
        }
        else {
            Add-HarnessResult 'neg_project_exists' 'FAIL' ((Get-ErrorText $neg3) + '. Verifique se o Resolve criou um segundo projeto com outro nome.')
        }
    }
    else {
        Add-HarnessResult 'neg_project_exists' 'NOT_RUN' 'Requer apply_assembly anterior bem-sucedido e sem troca pendente.'
    }

    # N4. Midia ausente: manifesto com caminhos inexistentes (DENTRO de 1-Bruto, para o teste ser de "ausente" e nao de
    # "fora da pasta") em projeto TravelCut_ novo.
    if ($probeOk -and -not $switchBlocked) {
        $missingName = 'TravelCut_Harness_NegMedia_' + $utc.TrimEnd('Z')
        $missingDoc = Get-Content -LiteralPath $assemblyFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $missingDoc.editorProjectName = $missingName
        $missingBruto = Join-Path $allowedRoot '..\..\1-Bruto\nao-existe'
        foreach ($m in @($missingDoc.media)) {
            $m.absolutePath = [System.IO.Path]::GetFullPath((Join-Path $missingBruto ([System.IO.Path]::GetFileName([string]$m.absolutePath))))
        }
        $missingFile = Join-Path $artifactDir 'assembly-missing-media.json'
        [System.IO.File]::WriteAllText($missingFile, ($missingDoc | ConvertTo-Json -Depth 8), $Utf8NoBom)
        $neg4 = Invoke-Bridge -StepName 'neg_media_missing' -Command 'apply_assembly' -Payload @{
            manifestPath      = $missingFile
            allowedOutputRoot = $allowedRoot
            endFrameInclusive = $EndFrameInclusive
        } -TimeoutSec 300
        $hasMissing = $false
        if ($neg4.Response) {
            if ($neg4.Response.errorCode -eq 'MEDIA_NOT_FOUND') { $hasMissing = $true }
            if (@($neg4.Response.rejected | Where-Object { $_.code -eq 'MEDIA_NOT_FOUND' }).Count -gt 0) { $hasMissing = $true }
        }
        if ($hasMissing) {
            Add-HarnessResult 'neg_media_missing' 'PASS' ("MEDIA_NOT_FOUND reportado (status=$($neg4.Response.status)); projeto de teste '$missingName' pode ter sobrado vazio no Project Manager.")
        }
        else {
            Add-HarnessResult 'neg_media_missing' 'FAIL' (Get-ErrorText $neg4)
        }
    }
    else {
        Add-HarnessResult 'neg_media_missing' 'NOT_RUN' 'Probe falhou ou troca de projeto pendente.'
    }

    # N5. Resolve fechado: so verificavel quando o Resolve ja esta fechado (o harness nao fecha o Resolve).
    if (-not $resolveRunning) {
        if ($probe.Response -and @('SCRIPTING_DISABLED', 'NOT_INSTALLED') -contains $probe.Response.errorCode) {
            Add-HarnessResult 'neg_resolve_closed' 'PASS' ("Resolve fechado: bridge recusou com " + $probe.Response.errorCode + '.')
        }
        else {
            Add-HarnessResult 'neg_resolve_closed' 'FAIL' (Get-ErrorText $probe)
        }
    }
    else {
        Add-HarnessResult 'neg_resolve_closed' 'NOT_RUN' 'Resolve aberto. Feche o Resolve manualmente e rode -Only probe (ver roteiro).'
    }

    # N6. Timeout: probe com limite de 0 s deve ser encerrado pelo harness (sem efeito colateral).
    $neg5 = Invoke-Bridge -StepName 'neg_timeout' -Command 'probe' -TimeoutSec 0
    if ($neg5.ProcessStatus -eq 'TimedOut') {
        Add-HarnessResult 'neg_timeout' 'PASS' 'Processo do bridge encerrado por timeout (arvore encerrada).'
    }
    else {
        Add-HarnessResult 'neg_timeout' 'FAIL' ("processo terminou antes do limite: $($neg5.ProcessStatus)")
    }

    # N7. Operacao nao suportada: so testavel se o probe reportou metodo ausente (false).
    # Metodo null = nao verificavel sem projeto aberto: nao vira PASS.
    if ($methodsMissing.Count -gt 0) {
        Add-HarnessResult 'neg_unsupported_operation' 'PASS' ("Metodos ausentes registrados como unsupported: " + ($methodsMissing -join ', '))
    }
    elseif ($probeOk -and $probe.Response.methods) {
        foreach ($prop in $probe.Response.methods.PSObject.Properties) {
            if ($null -eq $prop.Value) { $methodsUnverified += $prop.Name }
        }
        if ($methodsUnverified.Count -gt 0) {
            Add-HarnessResult 'neg_unsupported_operation' 'NOT_RUN' ("Metodos nao verificaveis sem projeto aberto: " + ($methodsUnverified -join ', ') + '. Abra um projeto TravelCut_ e repita.')
        }
        else {
            Add-HarnessResult 'neg_unsupported_operation' 'NOT_RUN' 'Todos os metodos presentes no probe; nada a validar nesta versao.'
        }
    }
    else {
        Add-HarnessResult 'neg_unsupported_operation' 'NOT_RUN' 'Probe falhou; nao ha metodos a validar.'
    }
}

# ---------------------------------------------------------------------------
$pass = @($script:Results | Where-Object { $_.status -eq 'PASS' }).Count
$fail = @($script:Results | Where-Object { $_.status -eq 'FAIL' }).Count
$notRun = @($script:Results | Where-Object { $_.status -eq 'NOT_RUN' }).Count

$summary = [ordered]@{
    schemaVersion = 'host-validation-davinci/1'
    runUtc        = $utc
    projectName   = $ProjectName
    endFrameInclusive = $EndFrameInclusive
    counts        = [ordered]@{ PASS = $pass; FAIL = $fail; NOT_RUN = $notRun }
    results       = @($script:Results)
    note          = 'Resultado de execucao no host. Mock/VM nao equivale a host_pass.'
}
[System.IO.File]::WriteAllText((Join-Path $run 'results.json'), ($summary | ConvertTo-Json -Depth 6), $Utf8NoBom)

Write-Host ''
Write-Host "Evidencias: $run"
Write-Host "PASS=$pass FAIL=$fail NOT_RUN=$notRun"
if ($fail -gt 0) { exit 1 }
exit 0
