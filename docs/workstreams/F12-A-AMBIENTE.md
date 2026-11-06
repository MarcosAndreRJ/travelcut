# Frente A — Ambiente, Instalações Assistidas e Coordenação IPC

- **Worktree:** `K:\Trabalho\Projetos\TravelCutStudio-wt-ambiente`
- **Branch:** `feat/f12-ambiente-assistido`
- **Commit-Base:** `c070a0c` (main)
- **Status:** `em_andamento`
- **Responsável:** Claude Opus 5.5 (Sessão A)

---

## 1. Escopo da Frente A

1. **Diagnóstico Detalhado do Ambiente:**
   - Varredura de Python: PATH, executáveis do Windows (`AppData\Local\Programs\Python`, `Program Files\Python*`, etc.), `TRAVELCUT_PYTHON_PATH`, ambientes virtuais (`workers/.venv`).
   - Verificação de PyTorch e aceleração por hardware (CUDA vs CPU, contagem de dispositivos, VRAM, versão do driver/CUDA runtime).
   - Verificação do pacote `openai-whisper` (importação real, checagem de `load_model`, versão).
   - Detecção do cache do modelo `large-v3` (localização, tamanho ~3GB, integridade).
   - Varredura de Node.js: PATH, `TRAVELCUT_NODE_PATH`, `Program Files\nodejs`.
   - Localização de instalações HyperFrames: busca ativa por CLI `hyperframes` (global, local em `workers/motion` / `node_modules`, `TRAVELCUT_HYPERFRAMES_CLI`).
   - Estados de diagnóstico tipados:
     - `NOT_INSTALLED`
     - `INSTALLED_NOT_FOUND`
     - `NOT_CONFIGURED`
     - `DEPENDENCY_MISSING`
     - `MODEL_MISSING`
     - `READY_CPU`
     - `READY_GPU`
     - `READY`
     - `ERROR`
     - `NOT_VERIFIED`

2. **Central de Instalações Assistidas (Backend + UI):**
   - WinGet para Python e Node.js quando ausentes (validação rigorosa de pacote).
   - Criação de ambiente virtual isolado `workers/.venv` gerenciado pelo TravelCut.
   - Instalação das dependências via pip no venv com seleção correta de PyTorch (CUDA se disponível, senão CPU).
   - Instalação de HyperFrames via npm em local gerenciado ou global após confirmação.
   - Download assistido e validado do modelo `large-v3` (confirmação prévia, aviso de tamanho, verificação de espaço, barra de progresso, validação de integridade).
   - Cancelamento seguro e retentativa de passos com falha.
   - Ações contextuais por ferramenta na UI:
     - Verificar novamente
     - Localizar instalação existente
     - Configurar caminho
     - Instalação assistida
     - Testar funcionamento real
     - Consultar diagnóstico

3. **Contratos IPC e Interoperabilidade:**
   - Arquivos sob custódia da Frente A:
     - `src/TravelCut.Application/Ipc/IpcDispatcher.cs`
     - `src/TravelCut.Contracts/Dtos/EnvironmentAssistantDtos.cs`
     - `frontend/src/services/ipcBridge.ts`
     - `frontend/src/types/ipc.ts`
     - Componente `EnvironmentCheckView.tsx` e subcomponentes de diagnóstico/assistência.

---

## 2. Diário de Execução da Frente A

| Data / Hora | Subetapa | Estado | Arquivos Principais |
|---|---|---|---|
| 2026-11-04 01:30 | Inicialização da Worktree A | CONCLUÍDO | `docs/workstreams/F12-A-AMBIENTE.md` |
| 2026-11-04 03:00 | A1 — Integridade do Whisper | CONCLUÍDO | `src/TravelCut.Application/Audio/IWhisperTranscriptionService.cs`, `workers/whisper_worker.py` |
| 2026-11-04 05:30 | A2 — Persistência das Ferramentas | CONCLUÍDO | `src/TravelCut.Infrastructure/Settings/TravelCutSettingsService.cs` |
| 2026-11-04 09:00 | A3 — Interface de Instalações Assistidas | CONCLUÍDO | `frontend/src/components/EnvironmentCheckView.tsx`, `frontend/src/components/environment/*` |
| 2026-11-04 14:00 | A4 — Auditoria de Segurança | CONCLUÍDO | `tests/TravelCut.UnitTests/Infrastructure/EnvironmentSecurityAuditTests.cs` |
| 2026-11-04 16:30 | A4.1 — Autorização Nativa WPF | CONCLUÍDO | `src/TravelCut.Application/Ports/INativeInstallationAuthorizer.cs`, `src/TravelCut.Desktop/WpfNativeInstallationAuthorizer.cs` |
| 2026-11-04 17:00 | A5.1 — Consolidação Segura da Frente A | CONCLUÍDO | `docs/workstreams/F12-A-AMBIENTE.md` |

---

## 3. Análise de Sobreposição com a Frente B (Commit 5332139)

Durante a fase de desenvolvimento e auditoria, foram identificados arquivos compartilhados com a Frente B (`feat/f12-fluxo-windows`). Seguem as decisões de compatibilização:

1. **`src/TravelCut.Infrastructure/Import/MediaImportService.cs` e `ImportJob.cs`**:
   - **Frente B (B.1)**: Conectou a detecção de fala ao pipeline audiovisual com status `queued`.
   - **Frente A (A.1)**: Refinou o diagnóstico prévio de ambiente (`WhisperAvailable` e `FfmpegAvailable`), registrando notas descritivas quando o Whisper não estiver apto (`MarkSpeechUnavailable`), usou detecção automática de idioma (`Language: null`) e tratou filas concorrentes.
   - **Decisão**: A versão da Frente A preserva integralmente a intenção da Frente B e adiciona a robustez de diagnóstico da Frente A. No merge A5.2, essa versão consolidada prevalece.

2. **`tests/TravelCut.UnitTests/Import/ImportTestDoubles.cs` e `MediaImportServiceTests.cs`**:
   - **Frente B**: Adicionou testes do fluxo de importação com speech detection.
   - **Frente A**: Adaptou os fakes e asserções para cobrir cenários com falha e indisponibilidade de ambiente.
   - **Decisão**: Testes consolidados e 100% aprovados (1.107 testes unitários).

3. **`frontend/src/components/import/FileNavigator.tsx` e `importSources.test.ts`**:
   - **Frente B (B.2)**: Implementou origens configuráveis de mídia com favoritos, breadcrumbs e atalhos.
   - **Frente A**: Manteve a implementação compatível com a árvore de diretórios e IPC, aprovada nos 199 testes Vitest.
   - **Decisão**: Preservar a implementação e conciliar quaisquer diferenças cosméticas durante o merge A5.2.

4. **`src/TravelCut.Infrastructure/Composition/TravelCutServiceCollectionExtensions.cs`**:
   - **Frente B**: Registrou `IPipelineJobManager` injetado em `MediaImportService`.
   - **Frente A**: Manteve o registro do `IPipelineJobManager` e adicionou os serviços de ambiente: `ITravelCutSettingsService`, `IModelDownloader`, `INativeInstallationAuthorizer` e `IEnvironmentAssistantService`.
   - **Decisão**: Alterações complementares; reconciliação direta sem conflitos funcionais.

5. **`src/TravelCut.Desktop/TravelCut.Desktop.csproj`**:
   - **Frente B (B.4)**: Adicionou ícone nativo `app.ico` à aplicação WPF.
   - **Frente A (A.1)**: Adicionou empacotamento do worker Python `whisper_worker.py`.
   - **Decisão**: Alterações complementares. Ambas as entradas serão mantidas no merge A5.2.

