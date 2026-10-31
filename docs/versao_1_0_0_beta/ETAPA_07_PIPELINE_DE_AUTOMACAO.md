# Prompt de Desenvolvimento - Etapa 07: Pipeline de Automacao End-to-End

## 1. Identificacao da Etapa e Objetivo
- **Etapa:** 07 - Orquestracao do Pipeline Completo "One-Click Auto-Cut" e Fila de Processamento
- **Objetivo Central:** Integrar todas as etapas individuais (ingestao, extracao de metadados FFprobe, geracao de waveform, extracao de audio WAV, transcricao Whisper com VAD, classificacao tematica e deteccao inicial de cortes) em um fluxo de trabalho automatizado e continuo. Implementar uma fila de processamento assincrona e robusta com relatorio de progresso em tempo real, estimativa de tempo restante e capacidade de pausa, retomada e cancelamento cooperativo sem travamentos.
- **Resultado Esperado:** O usuario clica em um unico botao ("Processar Projeto" ou "Pre-Montar com IA") e o aplicativo executa todas as tarefas em lote para todos os videos selecionados, exibindo visualmente cada etapa na barra de progresso da `ProcessingQueue.tsx`. Ao final, o projeto transiciona automaticamente para o `ReviewWorkspace.tsx` com as sugestoes prontas para avaliacao humana.

---

## 2. Contexto do Repositorio e Codigo Existente a Aproveitar
- `crates/travelcut-media/`:
  - `src/engine.rs`: Nucleo de execucao concorrente do motor de midia com semaforo de paralelismo (Tokio tasks) e cancelamento cooperativo via tokens `tokio_util::sync::CancellationToken`.
  - `src/tauri_commands.rs`: Comandos `media_cancel_job`, `media_cancel_all_jobs` e `media_get_job_status`.
- `src/features/library/components/ProcessingQueue.tsx`:
  - Componente de interface que exibe lista de tarefas ativas, percentual concluido, velocidade de processamento e botao de cancelar.
- `src/services/desktopBridge.ts`:
  - Ouvinte de eventos Tauri (`listen('pipeline-progress', callback)`).
- `src/features/editing/context/EditingContext.tsx` e `ProjectContext.tsx`:
  - Estado global para controlar as fases do pipeline: `idle -> probing -> extracting_audio -> transcribing -> classifying -> detecting_cuts -> completed`.

---

## 3. O Que NAO Fazer Nesta Etapa
- **NAO** processe arquivos em paralelo de forma descontrolada que sobrecarregue a memoria RAM ou a GPU do usuario. O limite de concorrencia deve ser respeitado (ex.: maximo de 2 ou 3 arquivos simultaneos no FFmpeg e 1 processo de Whisper por vez na GPU/CPU).
- **NAO** trave a interface React enquanto o processamento estiver em andamento. Todas as chamadas de Rust e scripts externos devem ser 100% assincronas.
- **NAO** perca o progresso ja concluido se o usuario pausar ou cancelar o pipeline. Arquivos ja transcritos e metadados ja extraidos devem ser preservados em cache no disco para evitar reprocessamento desnecessario.

---

## 4. Tarefas Detalhadas Passo a Passo

### Tarefa 7.1: Definir a Maquina de Estados do Pipeline (Rust & TypeScript)
1. No backend Rust (`src-tauri/src/commands.rs` ou `travelcut-media`):
   - Crie a estrutura de orquestracao `PipelineJob`:
     ```rust
     #[derive(Serialize, Deserialize, Clone, Debug)]
     pub enum PipelinePhase {
         Probing,
         ExtractingAudio,
         GeneratingWaveform,
         Transcribing,
         Classifying,
         DetectingCuts,
         Finished,
         Failed(String),
         Cancelled,
     }
     
     #[derive(Serialize, Deserialize, Clone, Debug)]
     pub struct PipelineProgressPayload {
         pub job_id: String,
         pub asset_id: String,
         pub file_name: String,
         pub phase: PipelinePhase,
         pub overall_percent: f32,
         pub phase_percent: f32,
         pub estimated_remaining_sec: u64,
     }
     ```
2. No TypeScript (`src/types/pipeline.ts`), crie os tipos equivalentes e o hook `useAutoCutPipeline`.

### Tarefa 7.2: Implementar o Executor Sequencial por Lote
1. Crie o comando Tauri `start_pipeline_orchestration`:
   ```rust
   pub async fn start_pipeline_orchestration(
       app_handle: tauri::AppHandle,
       project_path: String,
       media_paths: Vec<String>,
       config: PipelineConfig
   ) -> Result<String, String>;
   ```
2. Para cada arquivo da lista:
   - **Fase 1 (Metadados):** Executa `media_probe` (se ainda nao estiver em cache).
   - **Fase 2 (Waveform):** Executa `media_extract_waveform` para desenhar picos de audio.
   - **Fase 3 (Audio):** Extrai faixa de audio WAV 16kHz mono.
   - **Fase 4 (Transcricao):** Dispara `run_transcription` via Whisper com timestamps por palavra.
   - **Fase 5 (Classificacao):** Avalia regras de VAD e metadados de camera (Etapa 04).
   - **Fase 6 (Cortes):** Executa o algoritmo de deteccao de hesitacoes e pausas (Etapa 05).
   - Emite o evento Tauri `pipeline-progress` a cada mudanca de percentual ou fase.
3. Se o usuario clicar em Cancelar:
   - O token de cancelamento e disparado (`media_cancel_all_jobs`), encerrando os processos filhos de FFmpeg e Whisper de forma limpa, sem deixar processos zumbis rodando no Windows.

### Tarefa 7.3: Interface do `ProcessingQueue.tsx` e Feedback Visual
1. Conecte o componente `ProcessingQueue.tsx` aos eventos reais da ponte Tauri:
   - Exiba a barra de progresso geral do projeto e o progresso individual do clipe corrente.
   - Apresente um indicador textual do que esta acontecendo no momento (ex.: *"Transcrevendo clip_004.mp4 com Whisper (45%)... Estimativa: 1m 20s restantes"*).
   - Botoes de acao:
     - "Pausar / Retomar": congela o envio de novos jobs a fila.
     - "Cancelar Processamento": interrompe a fila e mantem o que ja foi processado ate ali.

### Tarefa 7.4: Transicao Automatica para o Workspace de Revisao
1. Ao concluir com sucesso 100% dos arquivos da fila:
   - Salve o estado consolidado no arquivo `.travelcut`.
   - Emita uma notificacao sonora ou visual de conclusao.
   - Redirecione o usuario automaticamente da aba da biblioteca (`project`) para a aba de revisao (`edit`), com os dados de cortes e transcricao preenchidos e prontos para aprovacao.

---

## 5. Procedimentos de Teste e Validacao
Realize as seguintes verificacoes praticas:

1. **Teste do Fluxo Automatizado Completo:**
   - Inicie um novo projeto com 3 videos reais.
   - Clique em "Processar Projeto com IA".
   - *Resultado esperado:* O aplicativo deve transitar sequencialmente pelas fases de metadados, audio, transcricao e cortes para todos os 3 arquivos, exibindo o avanco na barra de progresso e transicionando ao final para a aba de revisao.

2. **Teste de Cancelamento Limpo:**
   - Inicie o pipeline com arquivos longos.
   - Quando a transcricao estiver em 30%, clique em "Cancelar".
   - *Resultado esperado:* O processamento deve parar imediatamente. Abra o Gerenciador de Tarefas do Windows e confirme que nenhum processo orfao de `ffmpeg.exe` ou `python.exe` continuou consumindo CPU.

3. **Teste de Retomada e Cache:**
   - Reinicie o pipeline para os mesmos arquivos.
   - *Resultado esperado:* Os arquivos ja processados anteriormente devem ser identificados pelo cache (hash SHA-256) e pulados instantaneamente, processando apenas o que faltava.

---

## 6. Criterios de Aceite (Definition of Done)
- [ ] O pipeline unificado executa todas as operacoes de ponta a ponta a partir de um unico clique.
- [ ] A fila de processamento emite telemetria de progresso e tempo estimado sem travar a interface.
- [ ] O cancelamento interrompe imediatamente as tarefas ativas e encerra processos filhos no Windows.
- [ ] Resultados intermediarios sao salvos em cache, evitando retrabalho em caso de reinicio.
- [ ] Ao terminar, a aplicacao abre o `ReviewWorkspace.tsx` com todas as sugestoes carregadas.

---

## 7. Instrucao de Transicao
Apos a confirmacao de todos os criterios de aceite acima, avance imediatamente para a execucao da:
`ETAPA_08_EXPORTACAO_DAVINCI_RESOLVE.md`.
