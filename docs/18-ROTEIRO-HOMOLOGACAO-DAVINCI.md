# 18 — Roteiro de homologação do bridge DaVinci Resolve (host)

**Estado:** `host_pending`. Nada deste roteiro foi executado na VM de desenvolvimento (sem DaVinci, Python e FFmpeg).
Este documento só fecha a integração real quando o log de execução no host estiver anexado em
`progresso/HOST_TESTS.md`. Mock de teste não é prova de integração.

Ver também: `docs/10-DAVINCI-VM-HOST.md` (protocolo geral), `tools/host-validation/davinci/README.md` (kit),
`workers/README.md` (seção "Bridge DaVinci").

---

## 1. Pré-condições

| Item | Exigência | Como conferir |
|---|---|---|
| Resolve | **DaVinci Resolve Studio** (licença válida) | Help > About |
| Scripting externo | *Preferências > Sistema > Geral > External scripting using = **Local*** | Reinicie o Resolve após mudar |
| Python | 3.6+ **64 bits**, compatível com a versão do Resolve (consultar a tabela de compatibilidade da Blackmagic) | `py -3 -c "import sys,struct;print(sys.version, struct.calcsize('P')*8)"` |
| Variável | `TRAVELCUT_PYTHON` = caminho do `python.exe` escolhido (recomendado, evita o launcher `py`) | `echo %TRAVELCUT_PYTHON%` |
| Resolve path | `TRAVELCUT_RESOLVE_PATH` só se o Resolve não estiver em `%ProgramFiles%\Blackmagic Design\DaVinci Resolve\Resolve.exe` | Ver Explorer |
| FFmpeg | ≥ 5.1 com `drawtext` e `libx264` no PATH | `ffmpeg -version` e `ffmpeg -filters | findstr drawtext` |
| Projeto | Nenhum projeto pessoal será usado. O harness só cria projetos `TravelCut_Harness_*` | — |
| Projeto aberto | Se algum projeto estiver aberto, anote o nome: operações de escrita trocam o projeto atual | Project Manager |
| Build | `TravelCut.Desktop` e `workers/resolve_bridge.py` do mesmo commit; anotar hash | `git`/`dotnet --info` |

Registrar no formulário (`progresso/HOST_TESTS.md`): Windows (build), edição/versão do Resolve, versão do Python e bits,
caminho do Resolve, versão do FFmpeg, hash do build.

---

## 2. Preparação

```powershell
cd K:\...\TravelCutStudio\tools\host-validation\davinci
.\New-SyntheticMedia.ps1 -OutputDirectory "$env:TEMP\TravelCutHarness\media"
```

**Esperado:** 2 arquivos `synthetic_A_testsrc2_2997.mp4` e `synthetic_B_testsrc_2997.mp4`, 10 s, 1280x720, `r_frame_rate = 30000/1001`,
áudio AAC com bipe a cada segundo, `synthetic-media.json` com SHA-256. Abrir um dos arquivos no Resolve **não** é necessário.

Origem intacta: os arquivos de `%TEMP%` são sintéticos; o roteiro não grava em nenhuma pasta do usuário.

---

## 3. Execução

### 3.1 Roteiro completo

```powershell
.\Run-HostValidation.ps1 -MediaManifestPath "$env:TEMP\TravelCutHarness\media\synthetic-media.json"
```

Se houver projeto aberto no Resolve, o script para com `NOT_RUN` e pede `-ConfirmProjectSwitch`. Confirme a troca
(o projeto aberto **não** é salvo nem alterado pelo harness) e rode de novo com o switch.

### 3.2 Passos isolados (diagnóstico)

```powershell
.\Invoke-ResolveBridgeStep.ps1 -Command probe -StepName probe -EvidenceDir .\evidencias\manual
```

---

## 4. Passos, resultado esperado e critério

| # | Passo (`StepName`) | Comando do bridge | Resultado esperado | Critério |
|---|---|---|---|---|
| 1 | `probe` | `probe` | `ok:true`, `status` READY ou READY_PARTIAL; `productName`, `version` do Resolve; `methods` com 15 entradas (true/false/null) | PASS |
| 1a | `probe_product_studio` | — | `productName` contém "Studio" (a homologação exige Resolve Studio) | PASS, ou FAIL |
| 1b | `probe_open_project` | — | Informativo: nome do projeto aberto (se houver). **Registrado para a reversão (seção 6).** | informativo |
| 2 | `apply_assembly` | `apply_assembly` | `READY_REAL`; etapas `create_project`, `set_timeline_frame_rate`, `import_media`, `create_timeline`, `apply_edit_plan`, `query_timeline`, `save_project`, `create_snapshot` todas `ok`. Pedido leva `allowedOutputRoot` (`<proj>\3-Trabalho\editor`) e o `.drp` de snapshot fica sob ela | PASS só com READY_REAL |
| 2a | `apply_in_point` | — | `leftOffsetFrames` de cada clipe = `sourceStartFrame` do manifesto (tolerância 0) | PASS; FAIL se divergir; NOT_RUN se a API não expôs o leftOffset |
| 3 | `query_timeline` | `query_timeline` | 4 itens na trilha V1, `recordFrame`/`durationFrames` iguais aos esperados. Exige que o projeto `TravelCut_*` seja o **atual** do Resolve (senão `PROJECT_NOT_CURRENT`) | PASS |
| 4 | `export_project` | `export_project` | Arquivo `.drp` > 0 bytes em `evidencias\<utc>\proj\3-Trabalho\editor\harness-<utc>\` | PASS |
| 5 | `render_preview` | `render_preview` | MP4 em `...\3-Trabalho\editor\harness-<utc>\render`; tempo medido pelo harness (não estimado) | PASS |
| N1 | `neg_name_without_prefix` | `query_timeline` (nome sem prefixo) | `PERMISSION_DENIED` sem acesso ao Resolve | PASS |
| N2 | `neg_export_without_prefix` | `export_project` (nome sem prefixo) | `PERMISSION_DENIED`; nenhum arquivo | PASS |
| N3 | `neg_project_exists` | `apply_assembly` (mesmo nome) | `PROJECT_EXISTS`; nenhum projeto existente aberto | PASS |
| N4 | `neg_media_missing` | `apply_assembly` (mídia inexistente **dentro de 1-Bruto**, projeto novo) | `MEDIA_NOT_FOUND` por clipe (rejeição) ou erro com esse código | PASS |
| N5 | `neg_resolve_closed` | `probe` com Resolve fechado | `SCRIPTING_DISABLED` ou `NOT_INSTALLED` (só com o Resolve fechado) | PASS, ou NOT_RUN |
| N6 | `neg_timeout` | `probe` com limite 0 s | Processo encerrado pelo harness (`TimedOut`) | PASS |
| N7 | `neg_unsupported_operation` | — | Método `false` no probe: registrar como `unsupported` (PASS). Método `null` = não verificável sem projeto aberto: NOT_RUN, nunca PASS | PASS, ou NOT_RUN |

Numeração N1–N7 igual aos nomes de passo do `Run-HostValidation.ps1`. Passos que exigem projeto aberto (`query_timeline`,
`render_preview`, `export_project`) só valem com o projeto `TravelCut_*` como atual no Resolve.

Os 4 cortes da montagem são: `A` [30,150) e [180,270); `B` idem (fim exclusivo no manifesto). Em 29,97 fps isso
dá durações de 120 e 90 frames por corte, na sequência 0, 120, 210, 330.

---

## 5. Itens a confirmar no host (suposições da API)

Cada item abaixo é uma **suposição** do código. Confirmar com a documentação da versão instalada
(scripting README do Resolve) e com a observação no host. Registrar o resultado na tabela da seção 7.

1. **Convenção do `endFrame` em `AppendToTimeline`.** O manifesto usa fim exclusivo; o bridge envia
   `endFrame = fim - 1` por padrão (`endFrameInclusive=true`). Teste: rodar com `-EndFrameInclusive:$false`.
   A convenção correta é a que produz `durationFrames` iguais aos esperados no `query_timeline` (divergência de 1 frame indica
   convenção errada). Se a convenção mudar, atualizar `TRAVELCUT_RESOLVE_END_INCLUSIVE` e o padrão.
2. **`recordFrame` relativo a `timeline.GetStartFrame()`.** O bridge soma o início da timeline. Confirmar o valor
   de `GetStartFrame()` (costuma ser 86400 = 01:00:00:00 a 24 fps) e que a posição lida (`GetStart() - início`) bate com a enviada.
3. **`ImportMedia` com 29,97 fps e VFR.** Confirmar a duração e o frame rate reportados pelo item (`GetClipProperty`), e
   o comportamento com clipe de celular VFR (não usado no sintético; testar em mídia de teste separada, se houver).
4. **Casamento por caminho.** `GetClipProperty("File Path")` deve devolver o caminho que foi importado (mesmo com barras
   diferentes). Se não casar, o bridge rejeita as mídias com `MEDIA_NOT_FOUND`.
5. **Áudio vinculado.** `AppendToTimeline` com `trackIndex` e sem `mediaType` deve trazer o áudio do clipe. Confirmar no
   Resolve (aba de áudio, A1) que o áudio está sincronizado com o vídeo.
6. **`SetSetting("timelineFrameRate", "29.97")`** antes de importar. Confirmar que a timeline criada está em 29,97.
7. **`CreateProject` com nome existente** deve devolver `None`. Se o Resolve criar um segundo projeto com outro nome,
   o teste N3 falha e isso deve ser anotado (risco de projeto sobrando).
8. **`ExportProject(nome, caminho)`** gera `.drp` real. Confirmar que o arquivo reabre no Resolve.
9. **Render:** `SetCurrentRenderFormatAndCodec("mp4","H264")`, `SetRenderSettings` (`SelectAllFrames`, `TargetDir`,
   `CustomName`), `AddRenderJob`, `StartRendering([jobId])` (confirmar se a lista é aceita ou se é preciso `StartRendering()`),
   `IsRenderingInProgress`. Confirmar o nome final do arquivo gerado: o bridge usa `CustomName` único
   `TravelCut_preview_<yyyymmddThhmmss>` por job e só aceita arquivo cujo nome começa com ele.
10. **Métodos do probe.** `hasattr` sobre objetos Python do Resolve pode não refletir a API real. Comparar `methods` com o que
    a operação faz de fato.
11. **`GetLeftOffset`** (offset de origem do item = in-point): o bridge lê como `leftOffsetFrames` e o adaptador/harness
    comparam com `sourceStartFrame` do manifesto (tolerância 0, passo `apply_in_point`). Se a API expuser o valor em outra
    unidade (ex.: segundos ou timebase do clipe), a comparação falha: registrar o valor observado antes de ajustar.
12. **Versão e produto.** `GetProductName` e `GetVersionString` devolvem o texto esperado para a edição Studio.

---

## 6. Reversão (limpeza manual)

O harness **não apaga** projetos. Ao final:

1. Abra o **Project Manager** do Resolve.
2. Apague **somente** os projetos cujo nome começa com `TravelCut_Harness_` (incluindo `TravelCut_Harness_NegMedia_*`).
   Depois, reabra no Resolve o projeto que estava aberto antes da execução, registrado no passo `probe_open_project`
   do log (se o log não registrar projeto aberto, nada precisa ser reaberto). O bridge troca o projeto atual ao criar o
   projeto de homologação; a reabertura devolve o ambiente ao estado anterior.
   Confira o nome antes de excluir.
3. Não excluir mídia de origem. Os arquivos sintéticos ficam em `%TEMP%\TravelCutHarness`; apague a pasta quando quiser.
4. Se um projeto `TravelCut_*` tiver sido criado sem o harness, trate-o da mesma forma (verifique o nome primeiro).

Nunca apagar projetos sem o prefixo `TravelCut_`.

---

## 7. Evidências e tabela de capacidades

**Evidências:** pasta `tools/host-validation/davinci/evidencias/<utc>/` com `log.txt`, `results.json`, `assembly.json`,
`*.request.json`, `*.response.json`, `*.stderr.txt`, `export/`, `render/`. Anexar a `progresso/HOST_TESTS.md` (sem caminhos
pessoais e sem prints com dados privados). Capturas de tela do Resolve (timeline, áudio A1, Project Manager) são recomendadas.

**Tabela de capacidades (preencher no host).** Verificação: `documented` (só na documentação), `mock_pass` (só mock),
`host_pending` (não executado no host), `host_pass` (executado no host com evidência), `unsupported` (método ausente).

| Operação (`editor/1`) | Método(s) do bridge | Verificação atual no código | Resultado no host | Evidência |
|---|---|---|---|---|
| detect_capabilities | `probe` | host_pending | ☐ host_pass ☐ unsupported ☐ falhou | |
| create_project | `CreateProject` | host_pending | ☐ host_pass ☐ unsupported ☐ falhou | |
| import_media | `ImportMedia` + `GetClipProperty` | host_pending | ☐ host_pass ☐ unsupported ☐ falhou | |
| create_timeline | `CreateEmptyTimeline`, `SetCurrentTimeline` | host_pending | ☐ host_pass ☐ unsupported ☐ falhou | |
| apply_edit_plan | `AppendToTimeline`, `SetSetting`, `SaveProject`, `SetCurrentTimeline`, `GetMediaPool` | host_pending | ☐ host_pass ☐ unsupported ☐ falhou | |
| query_timeline | `GetItemListInTrack` (exige o projeto TravelCut_ atual; senão `PROJECT_NOT_CURRENT`) | host_pending | ☐ host_pass ☐ unsupported ☐ falhou | |
| export_preview | `SetRenderSettings`, `AddRenderJob`, `StartRendering`, `IsRenderingInProgress` | host_pending | ☐ host_pass ☐ unsupported ☐ falhou | |
| export_project | `ExportProject` | host_pending | ☐ host_pass ☐ unsupported ☐ falhou | |
| create_snapshot | `ExportProject` | host_pending | ☐ host_pass ☐ unsupported ☐ falhou | |

**Conclusão de homologação** (escolher uma e justificar com a evidência): ☐ `host_pass` completo ☐ `host_pass` parcial
(operações listadas) ☐ falhou (erros registrados) ☐ não executado.

---

## 8. Erros conhecidos e tratamento

| Código | Significado | Ação |
|---|---|---|
| `NOT_INSTALLED` | Módulo `DaVinciResolveScript` não carregou | Conferir `RESOLVE_SCRIPT_API`/`RESOLVE_SCRIPT_LIB` e a instalação |
| `SCRIPTING_DISABLED` | `scriptapp("Resolve")` retornou None | Resolve fechado, ou scripting externo em *Local* |
| `PYTHON_UNAVAILABLE` | Python não iniciou ou é 32 bits | Definir `TRAVELCUT_PYTHON` com Python 64 bits |
| `PROJECT_EXISTS` | Nome já existe ou criação recusada | Esperado no teste N3; em outros casos, usar outro nome |
| `PERMISSION_DENIED` | Nome sem prefixo `TravelCut_`, ou caminho fora de `3-Trabalho\editor` | Esperado nos testes N1/N2; caminho fora da raiz é bug do pedido |
| `PROJECT_NOT_CURRENT` | Projeto `TravelCut_*` pedido não é o atual do Resolve (o bridge não troca de projeto nessas operações) | Abrir o projeto `TravelCut_*` no Resolve e repetir |
| `MEDIA_NOT_FOUND` | Arquivo ausente ou não importado | Esperado no teste N4; caso contrário, conferir o caminho |
| `TIMEOUT` | Processo do bridge excedeu o limite | Conferir o log do Resolve; reexecutar com mais tempo |
| `BRIDGE_PROTOCOL_ERROR` | Saída do bridge não é JSON válido | Ver `*.stderr.txt` e `*.response.json` |
| `BRIDGE_EXCEPTION` | Exceção não prevista na API | Ver `*.stderr.txt`; anotar o tipo de exceção |
