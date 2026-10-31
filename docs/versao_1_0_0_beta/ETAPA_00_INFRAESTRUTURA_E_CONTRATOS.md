# Prompt de Desenvolvimento - Etapa 00: Infraestrutura e Contratos

## 1. Identificacao da Etapa e Objetivo
- **Etapa:** 00 - Infraestrutura, Tauri 2, Rust Engine e Contratos Compartilhados
- **Objetivo Central:** Estabelecer uma fundacao tecnica solida, integrada e livre de dados ficticios estruturais. Conectar o crate de motor nativo `crates/travelcut-media` diretamente ao executavel Tauri desktop (`src-tauri`), alinhar os contratos de tipagem entre Rust e TypeScript, unificar a ponte de comunicacao IPC (`desktopBridge.ts`) e estruturar o estado compartilhado global da aplicacao para suportar os fluxos das etapas seguintes.
- **Resultado Esperado:** O TravelCut Studio compila e executa no Windows como aplicativo desktop Tauri 2 nativo, comunicando-se perfeitamente com os comandos Rust de diagnostico, varredura e inspecao de midia, sem quebras de tipo ou rotas inexistentes.

---

## 2. Contexto do Repositorio e Codigo Existente a Aproveitar
Antes de escrever qualquer codigo novo, inspecione os seguintes arquivos fundamentais:
- `crates/travelcut-media/Cargo.toml` e `crates/travelcut-media/src/`:
  - `src/tauri_commands.rs`: Contem a definicao dos comandos Tauri para `media_initialize`, `media_probe`, `media_scan_directory`, `media_extract_waveform`, `media_generate_thumbnail`, `media_transcode`, `media_cancel_job` e `media_get_job_status`.
  - `src/models.rs`: Define os structs oficiais `MediaMetadata`, `StreamInfo`, `DiscoveredFile`, `InventoryOptions`, `WaveformData`, `ThumbnailOptions`, `JobStatus`.
  - `src/engine.rs`: Nucleo de execucao do motor de midia com pool de conexoes e fila cooperativa.
- `src-tauri/Cargo.toml`: Atualmente nao declara `travelcut-media` como dependencia de caminho local. Precisa referenciar `travelcut-media = { path = "../crates/travelcut-media" }`.
- `src-tauri/src/lib.rs` e `src-tauri/src/commands.rs`:
  - Atualmente contem funcoes duplicadas/simplificadas de `desktop_diagnostics`, `scan_project_folder`, `run_ffprobe`, `save_project_state`, `load_project_state`.
- `src/services/desktopBridge.ts`:
  - Contem chamadas `safeInvoke` usando `window.__TAURI_INTERNALS__`. Deve ser alinhado com o padrao oficial `@tauri-apps/api/core` para invocacao de comandos e tratamento de eventos.
- `src/features/media/types.ts` e `src/features/editing/types/editingTypes.ts`:
  - Modelos de dados TypeScript que devem espelhar fielmente os structs de Rust.

---

## 3. O Que NAO Fazer Nesta Etapa
- **NAO** recrie uma nova biblioteca de componentes UI ou altere o design system Tailwind.
- **NAO** reconstrua o parser de FFprobe ou o scanner de arquivos em Rust do zero; use exclusivamente o que ja esta implementado em `crates/travelcut-media`.
- **NAO** crie uma segunda camada de mock no frontend. O objetivo desta etapa e permitir que os testes de bridge retornem dados reais de ambiente do host Windows.
- **NAO** implemente rotas complexas de edicao de timeline ou transcricao ainda; concentre-se estritamente na conexao IPC e contratos.

---

## 4. Tarefas Detalhadas Passo a Passo

### Tarefa 0.1: Integrar `travelcut-media` em `src-tauri/Cargo.toml`
1. Abra `src-tauri/Cargo.toml` e adicione a dependencia local:
   ```toml
   [dependencies]
   travelcut-media = { path = "../crates/travelcut-media" }
   tauri = { version = "2", features = [] }
   serde = { version = "1.0", features = ["derive"] }
   serde_json = "1.0"
   tokio = { version = "1.41", features = ["full"] }
   chrono = { version = "0.4", features = ["serde"] }
   walkdir = "2.5"
   uuid = { version = "1.11", features = ["v4", "serde"] }
   thiserror = "2.0"
   ```
2. Unifique os modulos do workspace no `Cargo.toml` raiz se necessario para garantir resolucao deterministica de versoes.

### Tarefa 0.2: Conectar o MediaEngineState e Registrar Comandos em `src-tauri/src/lib.rs`
1. Em `src-tauri/src/lib.rs`, importe o estado e comandos de `travelcut_media::tauri_commands`:
   - Inicialize o `MediaEngineState` gerenciado pelo Tauri (`app.manage(MediaEngineState::new())`).
   - Registre a lista completa de comandos oficiais:
     - `commands::desktop_diagnostics` (diagnostico de sistema, versao do app, caminhos de ffmpeg/ffprobe)
     - `commands::save_project_state`
     - `commands::load_project_state`
     - `travelcut_media::tauri_commands::media_initialize`
     - `travelcut_media::tauri_commands::media_get_capabilities`
     - `travelcut_media::tauri_commands::media_probe`
     - `travelcut_media::tauri_commands::media_scan_directory`
     - `travelcut_media::tauri_commands::media_generate_thumbnail`
     - `travelcut_media::tauri_commands::media_generate_filmstrip`
     - `travelcut_media::tauri_commands::media_extract_waveform`
     - `travelcut_media::tauri_commands::media_transcode`
     - `travelcut_media::tauri_commands::media_cancel_job`
     - `travelcut_media::tauri_commands::media_cancel_all_jobs`
     - `travelcut_media::tauri_commands::media_get_job_status`

### Tarefa 0.3: Alinhar Contratos de Tipagem TypeScript (`src/types/` e `src/features/media/types.ts`)
1. Garanta que as interfaces TypeScript correspondam aos structs Serde retornados pelo Rust:
   ```typescript
   export interface RustMediaMetadata {
     file_path: string;
     file_name: string;
     format_name: string;
     duration_seconds: number;
     bit_rate: number;
     size_bytes: number;
     created_at_utc?: string;
     video_streams: RustVideoStreamInfo[];
     audio_streams: RustAudioStreamInfo[];
     camera_make?: string;
     camera_model?: string;
     color_space?: string;
     color_transfer?: string;
     color_primaries?: string;
     has_b_frames: boolean;
     is_hdr: boolean;
     rotation_degrees: number;
   }
   ```
2. Adicione os conversores bidirecionais (adaptadores) para converter `RustMediaMetadata` no modelo de interface `MediaAsset` utilizado pela UI do React.

### Tarefa 0.4: Modernizar `src/services/desktopBridge.ts`
1. Refatore a funcao `safeInvoke` para tentar invocar via `@tauri-apps/api/core` quando disponivel no contexto desktop ou `window.__TAURI_INTERNALS__`.
2. Forneca funcoes tipadas de alto nivel para:
   - `getDesktopDiagnostics(): Promise<DesktopDiagnosticsResult>`
   - `initializeMediaEngine(config?: Partial<MediaEngineConfig>): Promise<FfmpegCapabilities>`
   - `probeMediaFile(filePath: string): Promise<RustMediaMetadata>`
   - `scanProjectDirectory(dirPath: string): Promise<DiscoveredFile[]>`
   - `saveProjectFile(projectPath: string, payload: ProjectStatePayload): Promise<void>`
   - `loadProjectFile(projectPath: string): Promise<ProjectStatePayload>`
3. O modo fallback para browser web deve ser mantido estritamente para desenvolvimento de interface isolada, emitindo log com aviso `[DesktopBridge:WebFallback]`, sem mascarar erros quando em ambiente nativo Tauri.

### Tarefa 0.5: Estado Compartilhado Global (Context / Store)
1. Crie ou ajuste o contexto de gerenciamento de estado da aplicacao (`src/context/ProjectContext.tsx` ou unificacao com `EditingContext.tsx`) para manter:
   - Projeto ativo (nome, pasta raiz, data de criacao, caminho do arquivo `.travelcut`).
   - Lista de midias reais carregadas.
   - Status de conexao com o backend Rust nativo (conectado / desconectado / erro de binarios).
   - Diagnostico de hardware (disponibilidade de FFmpeg, FFprobe e aceleradores NVENC/QSV).

---

## 5. Procedimentos de Teste e Validacao
Apos implementar as mudancas, execute os seguintes passos de teste:

1. **Compilacao Rust e Testes Unitarios:**
   ```bash
   cargo check --manifest-path src-tauri/Cargo.toml
   cargo test --manifest-path crates/travelcut-media/Cargo.toml
   ```
   *Resultado esperado:* Compilacao sem erros e todos os testes unitarios de modelo e probe passando.

2. **Tipagem e Build do Frontend:**
   ```bash
   npm run build
   ```
   *Resultado esperado:* Sucesso na verificacao de tipos com `tsc` e bundle gerado pelo Vite sem avisos de tipagem ausente.

3. **Inspecao de Bridge Nativo:**
   Inicie a aplicacao em modo desenvolvimento ou teste a chamada de `desktop_diagnostics` garantindo que o retorno reflita o SO (`windows`), arquitetura (`x86_64`) e caminhos reais de `ffmpeg` e `ffprobe`.

---

## 6. Criterios de Aceite (Definition of Done)
- [ ] `src-tauri/Cargo.toml` referencia `travelcut-media` e compila com sucesso.
- [ ] Todos os 14 comandos Tauri nativos estao registrados em `src-tauri/src/lib.rs`.
- [ ] `desktopBridge.ts` expoe metodos tipados para todos os comandos de midia, persistencia e diagnostico.
- [ ] Interfaces TypeScript espelham com 100% de fidelidade os structs Serde do Rust.
- [ ] Nenhum mock ficticio substitui o retorno de diagnosticos quando em ambiente Tauri.
- [ ] O comando `npm run build` executa sem nenhum erro de compilacao TypeScript.

---

## 7. Instrucao de Transicao
Apos a confirmacao de todos os criterios de aceite acima, avance imediatamente para a execucao da:
`ETAPA_01_PROJETOS_E_MIDIAS_REAIS.md`.
