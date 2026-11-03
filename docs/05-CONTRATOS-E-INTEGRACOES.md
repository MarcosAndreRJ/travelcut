# Contratos entre módulos e serviços externos

## Regras gerais

DTOs versionados, JSON determinístico, campos obrigatórios e códigos de erro enumerados. Toda operação recebe correlationId, parâmetros validados, token de cancelamento (quando local) e contexto de projeto autorizado; execução de comandos não recebe strings shell montadas a partir do nome de arquivo.

## Frontend ⇄ .NET (WebView2)

**Exemplo de requisição de comando:**

```json
{"version":1,"requestId":"req-0001","type":"project.inventory.start","payload":{"projectId":"prj-01"}}
```

**Resposta:**

```json
{"version":1,"requestId":"req-0001","ok":true,"payload":{"jobId":"job-01","status":"queued"}}
```

**Evento assíncrono:**

```json
{"version":1,"type":"job.progress","payload":{"jobId":"job-01","completedUnits":12,"totalUnits":48,"status":"running"}}
```

Frontend jamais executa Python/FFmpeg diretamente. O host valida `type`, formato, projeto, limites de entrada e origem da mensagem.

### Métodos IPC implementados (2026-11-02)

Fonte de verdade dos DTOs: `src/TravelCut.Contracts/Dtos/*.cs`, espelhados em `frontend/src/types/ipc.ts`. Hoje o progresso é consultado por polling (`*.status`); o evento `job.progress` acima continua conceitual.

| Método | Request → Response | Observações |
|---|---|---|
| `project.create` / `project.open` / `project.recent` | `ProjectDtos.cs` | `status` = `created`/`opened_existing`; `createdFolders` reais; `estimatedCutDurationSeconds` = `null` até haver corte real; durações somam só mídias com `metadataStatus = ok`; `mediaWithoutMetadataCount` |
| `fs.listDirectory`, `dialog.selectFiles`, `dnd.resolvePaths` | `ImportDtos.cs` | somente leitura; `truncated` acima de 5000 entradas |
| `media.inspectCandidates`, `media.getThumbnail` | `ImportDtos.cs` | valores desconhecidos = `null`; miniatura `ffmpeg_unavailable` sem FFmpeg |
| `media.import.start/status/cancel` | `ImportDtos.cs` | ingestão aditiva (ADR-013) |
| `analysis.environment`, `pipeline.start/status/cancel`, `analysis.getMedia` | `SpeechAnalysisDtos.cs` | nada sintetizado: `unavailable` com motivo; faixas qualitativas; denominadores reais |
| `suggestions.list` `{projectId}` | `EditDtos.cs` | `kind` minúsculo (`silence`, `pause`, `filler`…); `status` = `Proposed`/`Approved`/`Rejected`/`Deferred` |
| `system.openFolder` `{path}` | `{opened, path, selectedFile}` | só caminhos absolutos dentro de um projeto conhecido; sem UNC; `explorer.exe` com ArgumentList |

Erros: `INTERNAL_ERROR` traz apenas "Erro interno (ref &lt;requestId&gt;)" — detalhes só no log. Mensagens WebView2 de origem diferente de `https://travelcut.studio` (ou `http://localhost:5173` em DEBUG) são ignoradas.

## Processos Python/FFmpeg

Contrato de worker: stdin JSON/arquivo JSON de entrada; stdout JSON de resultado com `schemaVersion`, `status`, `artifacts`, `warnings`, `errors`; stderr para logs técnicos. Para jobs longos, gerar eventos progressivos delimitados por linha e autenticar o processo pelo executável permitido, não por PATH arbitrário. `ProcessStartInfo.ArgumentList` e timeout; nada de `cmd /c` com interpolação de usuário.

**Whisper normalizado:**

```json
{"text":"Estamos chegando...","audio_duration_secs":34.2,
 "words":[{"text":"Estamos","start":0.1,"end":0.5},{"text":"chegando","start":0.51,"end":1.0}],
 "model":"large-v3","language":"pt"}
```

Timestamps não devem ser preenchidos artificialmente. Se o worker fornecer segmentos mas não palavras, status deve indicar granularidade inferior.

## `IEditorAdapter` (conceitual)

| Operação | Resultado obrigatório |
|---|---|
| `DetectCapabilities` | `mode`: mock/real; funções suportadas; motivo de indisponibilidade |
| `CreateProject` | editorProjectId, status, evidence |
| `ImportMedia` | mediaMappings, errors |
| `CreateTimeline` | timelineId, timebase, status |
| `ApplyEditPlan` | appliedClipMappings, rejectedOps, snapshotRef |
| `QueryTimeline` | revision/hash e clipes reais observados |
| `ExportPreview` | output path, duration, codec, evidence |
| `ExportProject` | caminho e formato nativo, se disponível |

Estados `SIMULATED`, `UNAVAILABLE`, `READY_REAL`, `PARTIAL_REAL`, `ERROR` e capabilities por operação. `mock` não deve criar `.drp` fictício como prova real.

## MCP C#

Usar SDK oficial https://github.com/modelcontextprotocol/csharp-sdk e operações **finas** expostas do mesmo Application Core. Para mutação: conferir projeto autorizado, revisão-base, idempotencyKey, `dryRun` possível e confirmação de ação sensível.

**Estado real (código em `src/TravelCut.Mcp`, detalhes em [docs/19](19-MCP-TRAVELCUT.md)):**

| Ferramenta MCP | Tipo | Rota IPC | Observação |
|---|---|---|---|
| `list_projects` | leitura | `project.recent` | filtrado pelos projetos autorizados |
| `get_inventory` | leitura | `media.list` | |
| `get_transcript` | leitura | `analysis.getMedia` | só campos de transcrição; conteúdo sensível |
| `get_suggestions` | leitura | `suggestions.list` | filtro opcional de status |
| `get_edit_plan` | leitura | `plan.get` | revisão opcional |
| `job_status` | leitura | `pipeline.status` | `jobId` opcional; diferente da atual = `JOB_NOT_FOUND` |
| `get_editor_capabilities` | leitura | `editor.capabilities` | |
| `get_motion_capabilities` | leitura | `motion.capabilities` | |
| `submit_review` | mutação | `suggestions.list` + `decision.submit` | nome real em vez de `submit_edit_decisions`; exige `idempotencyKey` |
| `queue_analysis` | mutação | `pipeline.start` | exige `idempotencyKey` |
| `apply_plan` | mutação | `editor.prepareApply` (dry-run) / `editor.apply` | dry-run por padrão; aplicar exige `revision`, fingerprint e `confirmProjectSwitch` |

Leituras sempre registradas; mutações só com `--allow-mutations`. Ainda não expostos (rascunho antigo): `prepare_host_validation`, cancelamento de job e `baseRevision` (compare-and-swap) em `decision.submit`.

Códigos próprios do MCP: `MUTATIONS_DISABLED`, `PROJECT_NOT_AUTHORIZED`, `INVALID_ARGUMENT`, `IDEMPOTENCY_KEY_REUSED`, `CONFIRMATION_REQUIRED`, `SUGGESTION_NOT_FOUND`, `JOB_NOT_FOUND`, `INTERNAL_ERROR`. Demais códigos vêm do dispatcher sem alteração.

## Versionamento de contratos

SchemaVersion obrigatório; rejeitar versões maiores desconhecidas, migrar versões compatíveis de modo explícito; fixtures compartilhados entre frontend, C# e Python. Tests de ida-e-volta JSON e de compatibilidade anterior. Contratos não podem ser alterados silenciosamente por subagentes.

## Erros estruturados

`MEDIA_NOT_FOUND`, `SOURCE_CHANGED`, `INVALID_TIMEBASE`, `JOB_CANCELLED`, `MODEL_UNAVAILABLE`, `EDITOR_UNAVAILABLE`, `CAPABILITY_UNSUPPORTED`, `PROJECT_VERSION_CONFLICT`, `DISK_FULL`, `PERMISSION_DENIED`, `CONTRACT_VERSION_MISMATCH`, `PROJECT_EXISTS`, `PROJECT_NOT_CURRENT`, `TIMEOUT`, `IDEMPOTENCY_KEY_REUSED`, `RECORD_NOT_SAVED`, `CONFIRMATION_REQUIRED`, `BRIDGE_PROTOCOL_ERROR`, `OPERATION_IN_PROGRESS` (trava entre processos app↔MCP ocupada), `PIPELINE_IN_PROGRESS`, `PROJECT_ROOT_MISSING`, `LOCKS_FOLDER_UNAVAILABLE`, `LOCK_ACCESS_DENIED`, `LOCK_IO_ERROR`, `SCHEMA_MIGRATION_IN_PROGRESS`, `SCHEMA_TOO_NEW` (banco gravado por build mais novo: não migrar), `NOT_READY`/`TOO_NEW` (inspetor de schema do MCP), `MUTATIONS_DISABLED`, `PROJECT_NOT_AUTHORIZED`, `CLI_UNSUPPORTED_TYPE`, `NODE_PATH_NOT_FOUND`, `OUTPUT_MISSING`. `EDITOR_UNAVAILABLE` é o código para qualquer indisponibilidade do editor em resultados; o estado específico (`NOT_RUNNING`, `SCRIPTING_DISABLED`, `PYTHON_UNAVAILABLE` etc.) vai na mensagem. UI traduz causa e possível ação; não engole erro.
