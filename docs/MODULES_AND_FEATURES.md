# Detalhamento de Modulos e Funcionalidades

Este documento cataloga todos os modulos, componentes e arquivos implementados no **TravelCut Studio**, explicitando o papel e a finalidade de cada elemento na solucao.

---

## 1. Fase 1: Design System & Fundacao Visual NLE

### Finalidade
Fornecer a infraestrutura visual profissional para software desktop de edicao de video, com paleta escura (estilo DaVinci Resolve), componentes acessiveis e layout estrutural desacoplado.

### Arquivos e Componentes
- `tailwind.config.js`: Define os tokens de design (`studio-bg`, `studio-panel`, `studio-sidebar`, `studio-card`, `studio-primary`, `studio-turquoise`, `studio-border`, etc.) e tipografia mono/sans.
- `src/components/layout/AppShell.tsx`: Casca global do aplicativo contendo a barra lateral persistente recolhivel, cabecalho contextual superior, area de visualizacao central e espaco inferior para acoplamento de timeline.
- `src/components/layout/Sidebar.tsx`: Navegacao principal entre as etapas de producao:
  - Home (Projetos e painel de controle)
  - Project (Biblioteca de midias e ingestao)
  - Edit (Revisao e cortes assistidos por IA)
  - Narrative (Estruturacao de capitulos e roteiro)
  - Export (Finalizacao e exportacao NLE)
- `src/components/layout/Header.tsx` e `TitleBar.tsx`: Cabecalho com botoes de acao rapida, seletor de projeto ativo e integracao com barra de titulo desktop.
- `src/components/ui/`: Biblioteca completa de componentes atomicos e moleculares:
  - `Button.tsx`: Botoes estilizados com suporte a variantes (`primary`, `secondary`, `ghost`, `danger`, `accent`), tamanhos e estados de carregamento.
  - `Card.tsx`: Cards de dados com header, content e footer, suportando modo interativo com elevacao no hover.
  - `Badge.tsx`: Emblemas com suporte a status de processamento, cameras e codecs.
  - `Input.tsx` e `Select.tsx`: Controles de formulario estilizados para ambiente escuro.
  - `ProgressBar.tsx`: Indicadores visuais de progresso com porcentagem e cores contextuais.
  - `Tooltip.tsx`: Exibicao de atalhos de teclado e dicas contextuais de ferramentas.
  - `Modal.tsx`: Janelas modais com bloqueio de foco e fundo com desfoque.

---

## 2. Fase 2: Home Dashboard & Gestao de Projetos

### Finalidade
Permitir ao criador de conteudo iniciar novos projetos rapidamente a partir de modelos pre-configurados para tipos de viagem, gerenciar projetos recentes e monitorar o status do ambiente de trabalho.

### Arquivos e Componentes
- `src/features/home/HomeDashboardView.tsx`: Painel principal integrado com todas as secoes de inicio.
- `src/features/home/components/HeroProjectBanner.tsx`: Destaque visual do projeto em andamento mais recente, permitindo retomada imediata com um clique.
- `src/features/home/components/ProjectTemplatesSection.tsx`: Grade de modelos de projeto otimizados para nichos de viagem:
  - *Expedicao & Trekking* (prioridade para audio de camera acao e drones)
  - *Vlog Urbano / Mochilao* (corte agressivo de pausas e ruidos de transito)
  - *Roteiro Gastronomico* (enfase em voz clara e planos de detalhe)
  - *Docu-Serie de Viagem* (preservacao de pausas emocionais e entrevistas)
- `src/features/home/components/RecentProjectsGrid.tsx`: Visualizacao e filtragem de projetos salvos localmente, com status de conclusao e contagem de midias.
- `src/features/home/components/QuickActionsBar.tsx`: Acoes diretas de criacao: "Novo Projeto", "Importar Gravacao", "Abrir Projeto Existente".
- `src/features/home/components/SystemPerformanceCard.tsx`: Monitoramento de hardware: espaco em disco, aceleracao por GPU ativa e versao do motor de IA.
- `src/features/home/data/mockHomeData.ts`: Colecao de dados de demonstracao representando projetos reais de expedicao (ex.: *Expedicao Roraima 2026*).

---

## 3. Fase 3: Biblioteca de Midias & Transcricao

### Finalidade
Ingestar dezenas de gigabytes de filmagens brutas, extrair metadados tecnicos, gerenciar a fila de transcricao de audio e permitir inspecao inicial das gravacoes e textos gerados.

### Arquivos e Componentes
- `src/features/library/MediaLibraryWorkspace.tsx`: Workspace principal integrado na aba `project`.
- `src/features/library/components/ProjectHeader.tsx`: Edicao inline do titulo do projeto, informacoes de resolucao nativa e stepper de 5 etapas do fluxo de trabalho.
- `src/features/library/components/ProjectStatsCards.tsx`: Quatro cartoes de metricas consolidadas: total de arquivos, duracao bruta acumulada, espaco consumido e volume de cortes recomendados.
- `src/features/library/components/MediaGrid.tsx`: Visualizacao flexivel em grade ou lista de clipes com identificacao de equipamento de filmagem (Sony FX3, DJI Mini 4 Pro, GoPro Hero 12), resolucao, framerate e status da transcricao Whisper.
- `src/features/library/components/ProcessingQueue.tsx`: Gaveta recolhivel que exibe o progresso percentual e a etapa atual de processamento de cada clipe (extracao de audio, transcricao, deteccao de cortes).
- `src/features/library/components/VideoPlayerPreview.tsx`: Player de video NLE integrado com barra de transporte, timecode preciso, controles de salto (-5s / +5s) e seletor de faixas de audio.
- `src/features/library/components/TranscriptInspector.tsx`: Inspecao detalhada da transcricao sincronizada com o player, com realce de hesitacoes vocais, silencios e busca por palavras-chave.
- `src/features/library/components/ProjectSummaryCard.tsx`: Resumo executivo com estimativa de tempo economizado e botao de avanco para a etapa de edicao.
- `src/features/media/types.ts`: Contrato formal de tipos (`MediaAsset`, `TranscriptWord`, `TranscriptSegment`, `CutSuggestion`) para consumo pela Fase 4.

---

## 4. Fase 4: Workspace de Revisao, Edicao em Texto & Exportador DaVinci

### Finalidade
Possibilitar a edicao rapida de video atraves da manipulacao direta do texto transcrito, aceitacao em massa ou individual de cortes sugeridos por IA e geracao de arquivos FCPXML/EDL para montagem final no DaVinci Resolve.

### Arquivos e Componentes
- `src/features/editing/ReviewWorkspace.tsx`: Workspace completo de revisao integrando player de video sincronizado, editor de transcricao interativo e painel de cortes recomendados.
- `src/features/editing/context/EditingContext.tsx`: Gerenciador de estado global da edicao:
  - Mantem lista de cortes sugeridos, aceitos e rejeitados.
  - Controla o timecode atual do player e a palavra ativa na transcricao.
  - Sincroniza a selecao textual com os trechos da timeline.
  - Fornece metodos de alteracao de status: `acceptCut`, `rejectCut`, `acceptAllCuts`, `resetCuts`.
- `src/features/editing/components/CutsListPanel.tsx`: Painel lateral agrupando cortes por categoria (hesitacoes, silencios mortos, redundancias, falhas tecnicas), com filtro e acoes em lote.
- `src/features/editing/components/TranscriptEditor.tsx`: Canvas de edicao textual onde palavras marcadas para remocao recebem estilo riscado (strike-through) e cores indicativas, permitindo ao usuario clicar em qualquer palavra para reposicionar o player de video imediatamente.
- `src/features/editing/components/EditingTimelinePreview.tsx`: Representacao grafica dos segmentos de video mantidos (em verde) e trechos descartados (em vermelho/cinza escuro), demonstrando visualmente o resultado do corte final.
- `src/features/editing/utils/davinciExporter.ts`: Modulo utilitario para sintese de arquivos de intercaminho profissional:
  - Exportacao para **FCPXML 1.8** com marcadores de cor no DaVinci Resolve (roxo para cortes de IA, ciano para selecoes do usuario).
  - Exportacao para **EDL (Edit Decision List CMX 3600)** para mesas de corte legadas.

---

## 5. Fase 6: Engine de Midia em Rust (`crates/travelcut-media`)

### Finalidade
Processamento de midia com desempenho nativo, sem travamentos na interface de usuario, operando via processos independentes ou comandos Tauri no desktop.

### Estrutura e Modulos Rust
- `src/config.rs`: Configuracoes de processamento de midia: caminhos de cache, diretorios temporarios, resolucoes de proxy, limites de memoria e quantidade de threads de trabalho.
- `src/models.rs`: Estruturas de dados tipadas com Serde (`MediaMetadata`, `StreamInfo`, `WaveformData`, `ThumbnailOptions`, `TranscodeProfile`, `JobStatus`).
- `src/error.rs`: Enum de erros robusto com tratativa para falhas de IO, execucao de FFmpeg, parsing JSON e timeouts de processo.
- `src/inventory/`:
  - `scanner.rs`: Varredura recursiva de pastas em disco, classificando arquivos em video, audio, imagem ou rejeitados (arquivos ocultos e de sistema como `.DS_Store`, `Thumbs.db`).
- `src/probe/`:
  - `ffmpeg_probe.rs`: Execucao de `ffprobe -v quiet -print_format json -show_format -show_streams`.
  - Parser para taxas de quadro fracionarias (ex.: `24000/1001` -> 23.976 fps, `60000/1001` -> 59.94 fps).
  - Deteccao de rotacao embutida nos metadados de containers MP4/MOV para orientacao correta de filmagens verticais.
  - Extracao de informacoes de espaco de cor, gama e perfis HDR (HLG, HDR10, Apple Log, S-Log3).
- `src/cache/`:
  - `cache_key.rs`: Gerador deterministico de identificadores unicos baseado em hash SHA-256 combinando caminho do arquivo, tamanho em bytes e data de modificacao. Impede processamento duplicado de arquivos identicos.
- `src/waveform/`:
  - `generator.rs`: Extracao de audio PCM e calculo de picos min/max em resolucoes dinamicas para renderizacao de formas de onda suaves na timeline NLE.
- `src/thumbnails/`:
  - `generator.rs`: Extracao precisa de frames individuais em instantes de tempo especificos e montagem de filmstrips compostos para navegacao visual rapida na timeline.
- `src/transcode/`:
  - `proxy_generator.rs`: Geracao de arquivos de baixa resolucao (Proxy 720p/1080p) para reproducao leve durante a edicao.
  - `hardware.rs`: Inspecao automatica de aceleradores de hardware suportados pelo sistema:
    - **NVIDIA NVENC** (`h264_nvenc`)
    - **Intel QuickSync** (`h264_qsv`)
    - **Apple VideoToolbox** (`h264_videotoolbox`)
    - **Linux VAAPI** (`h264_vaapi`)
    - Fallback seguro para CPU (`libx264` / `prores_ks`)
- `src/queue/`:
  - `job_queue.rs`: Fila de tarefas assincrona com Tokio e Rayon. Suporta prioridades, limite rigido de tarefas simultaneas (bounded concurrency) para evitar esgotamento de memoria RAM, monitoramento de progresso e cancelamento sob demanda.
- `src/tauri_commands.rs`: Comandos IPC prontos para consumo pela interface desktop via Tauri:
  - `probe_media_file`
  - `generate_thumbnail`
  - `generate_waveform`
  - `create_proxy`
  - `cancel_job`

---

## 6. Fase 7: Pesquisa de Transcricao, VAD & Word Alignment (`research/transcription`)

### Finalidade
Desenvolver, validar e otimizar algoritmos de reconhecimento de voz e alinhamento temporal fino especialmente calibrados para as condicoes desafiadoras de vlogs de viagem (ruido de vento, agua, motores de drone, musica ambiente, multiplos falantes).

### Modulos Python
- `contracts.py`: Dataclasses e tipagem estrita para toda a comunicacao de pesquisa:
  - `TranscriptionDocument`: Documento raiz contendo metadados de gravacao, metricas de processamento, lista de segmentos e lista de palavras.
  - `TranscriptSegment`: Frases com timecodes absolutos e confianca do modelo.
  - `TranscriptWord`: Palavras individuais com carimbo de milissegundos (`start_ms`, `end_ms`), confianca acustica e sinalizador de hesitacao (`is_hesitation`).
  - `SpeechInterval` e `SilenceInterval`: Segmentos de presenca e ausencia de voz classificados.
  - `SilenceCategory`: Enum de classificacao (`PAUSE_WORD`, `PAUSE_SENTENCE`, `REMOVABLE_GAP`, `DRONE_SCENERY_PROTECTED`).
  - `CutSuggestion`: Proposta formal de corte pronta para exportacao para a Fase 4.
- `audio.py`: Utilitarios de carregamento de audio WAV, leitura de cabecalho RIFF, conversao e normalizacao para PCM 16kHz mono em ponto flutuante (-1.0 a +1.0). Inclui sintetizador de sinais acusticos para geracao deterministica de testes sem dependencia de arquivos externos pesados.
- `vad.py`: Detector de Atividade Vocal proprietario com:
  - Estimativa dinamica do piso de ruido ambiente.
  - Calculo combinado de energia RMS e fluxo espectral (spectral flux).
  - Suavizacao por hangover para evitar truncamento em finais de frases suaves.
  - Integracao de dicas de contexto de viagem para protecao de cenas contemplativas.
- `silence_detector.py`: Analise editorial de intervalos de silencio, aplicando regras de corte cinematografico e preservando pausas essenciais a narrativa de viagem.
- `alignment.py`: Alinhador temporal fino baseado em Dynamic Time Warping (DTW) e modelos acusticos de duracao fonetica. Identifica e etiqueta marcadores de hesitacao em Portugues (*"eh"*, *"hum"*, *"tipo assim"*, *"ne"*, *"quer dizer"*).
- `engine.py`: Abstracao do motor de ASR:
  - `MockTranscriptionEngine`: Motor sintetico de execucao ultrarrapida para testes de integracao deterministica.
  - `FasterWhisperEngine`: Motor de producao baseado em `faster-whisper` (CTranslate2) com suporte a modelos `tiny`, `base`, `small`, `medium` e `large-v3`, quantizacao int8 e aceleracao CUDA.
- `sample_generator.py`: Gerador de 15 cenarios acusticos de pesquisa cobrindo todo o espectro de filmagens externas de viagem.
- `benchmark.py`: Suite de medicao de desempenho em lote, calculando Real-Time Factor (RTF), vazao de palavras por segundo, latencia e consumo de memoria, com gerador automatico de relatorios em Markdown e JSON.
- `cli.py`: Ferramenta de linha de comando para uso em scripts de automacao ou invocacao pelo backend Rust, com suporte a saidas nos formatos `json`, `phase4`, `txt` e `srt`.
