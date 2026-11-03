# Harness de homologação do bridge DaVinci (host)

Kit para rodar **no host** (Windows com DaVinci Resolve Studio) o roteiro de `docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md`.
Não instala nada, não abre o Resolve e não toca projetos sem prefixo `TravelCut_`.

**Estado:** scripts escritos e revisados por leitura na VM de desenvolvimento, **não executados** (a VM não tem
DaVinci, Python nem FFmpeg). O resultado só vale como `host_pass` após execução humana no host com o log anexado.

## Arquivos

| Arquivo | Função |
|---|---|
| `New-SyntheticMedia.ps1` | Gera 2 clipes H.264/AAC 29,97 fps de 10 s com timecode queimado e bipe por segundo, em pasta temporária. Não sobrescreve. |
| `New-SyntheticAssembly.ps1` | Gera um `editor-assembly/1` com 2 cortes de cada mídia (4 clipes), fingerprint `harness`. |
| `Invoke-ResolveBridgeStep.ps1` | Envia um comando ao `workers/resolve_bridge.py` e grava pedido, resposta e stderr em `evidencias/<utc>/`. |
| `Run-HostValidation.ps1` | Roteiro completo: probe → apply → query → export → render → negativos. Cada passo sai como PASS, FAIL ou NOT_RUN. |
| `evidencias/` | Saída das execuções (ignorada pelo git, contém caminhos locais). |

## Pré-requisitos no host

- Windows PowerShell 5.1 ou PowerShell 7.
- FFmpeg ≥ 5.1 com `drawtext` e `libx264` no PATH (ou `-FfmpegPath`/`-FfprobePath`).
- Python 3.6+ **64 bits** compatível com o Resolve (`TRAVELCUT_PYTHON` aponta para o `python.exe`).
- DaVinci Resolve Studio aberto para os passos que escrevem; scripting externo em
  *Preferências > Sistema > Geral > External scripting using = Local*.

## Uso

```powershell
cd K:\...\TravelCutStudio\tools\host-validation\davinci

# 1. Mídia sintética em pasta TEMPORÁRIA (sob %TEMP%)
.\New-SyntheticMedia.ps1 -OutputDirectory "$env:TEMP\TravelCutHarness\media"

# 2. Roteiro completo (convenção de endFrame inclusiva por padrão)
.\Run-HostValidation.ps1 -MediaManifestPath "$env:TEMP\TravelCutHarness\media\synthetic-media.json"

# 3. Mesma montagem com a outra convenção de endFrame (para decidir a convenção correta)
.\Run-HostValidation.ps1 -MediaManifestPath "..." -EndFrameInclusive:$false

# Se já houver projeto aberto no Resolve, confirme a troca explicitamente:
.\Run-HostValidation.ps1 -MediaManifestPath "..." -ConfirmProjectSwitch
```

Um passo isolado (por exemplo, só o probe):

```powershell
.\Invoke-ResolveBridgeStep.ps1 -Command probe -StepName probe -EvidenceDir .\evidencias\manual
```

## Regras

- Nomes de projeto: somente `TravelCut_...`. Os testes negativos usam um nome inventado sem prefixo
  (`Harness_SemPrefixo_Teste`) apenas para comprovar a recusa; o bridge recusa antes de acessar o Resolve.
- O harness **não fecha** o Resolve. O teste "Resolve fechado" só roda se o Resolve já estiver fechado.
- Evidências contêm caminhos locais e nomes de projeto. Não anexar a repositórios ou relatórios públicos sem revisão.
- Os scripts são ASCII puro (compatibilidade com Windows PowerShell 5.1).
- Artefatos (snapshot, `.drp`, MP4) ficam em `<projeto>\3-Trabalho\editor\harness-<utc>\`, a raiz permitida pelo bridge
  (`allowedOutputRoot`). Sem `-AssemblyPath`, o projeto é `<evidencias>\<utc>\proj` com as mídias sintéticas copiadas
  para `proj\1-Bruto`. Com `-AssemblyPath`, a raiz é derivada desse caminho e as mídias devem estar em `1-Bruto`/`2-Assets`
  do próprio projeto; nesse caso, a pasta `harness-<utc>` é criada dentro do projeto informado.
- Não grava nome de máquina (`COMPUTERNAME`) no log.

## Saídas

`evidencias/<utc>/`: `log.txt` (uma linha por passo), `results.json`, `assembly.json`,
`<passo>.request.json`, `<passo>.response.json`, `<passo>.stderr.txt`, `export/` (negativos), `proj/` (projeto temporário).
Preencher a tabela de capacidades em `progresso/HOST_TESTS.md` com base nessas evidências.
