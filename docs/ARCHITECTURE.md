# Arquitetura Tecnica do TravelCut Studio

Este documento descreve a arquitetura geral do **TravelCut Studio**, detalhando a divisao de responsabilidades entre as tres camadas principais da solucao, o fluxo de dados ponta a ponta e o modelo de integracao entre os subsistemas.

---

## 1. Visao Geral dos Subsistemas

O TravelCut Studio e projetado segundo um modelo de tres camadas complementares de alta eficiencia:

```
┌────────────────────────────────────────────────────────────────────────┐
│                        Camada 1: Desktop UI                           │
│              (React 18, TypeScript, Tailwind CSS, Tauri)               │
│                                                                        │
│  ┌──────────────────┐  ┌──────────────────┐  ┌───────────────────────┐ │
│  │  Home Dashboard  │  │  Media Library   │  │   Review Workspace    │ │
│  │   (Projetos e    │  │   & Player A/V   │  │  (Edicao em Texto,    │ │
│  │    Metricas)     │  │   (Transcricao)  │  │   Cortes IA, Timeline)│ │
│  └──────────────────┘  └──────────────────┘  └───────────────────────┘ │
└───────────────────────────────┬────────────────────────────────────────┘
                                │ IPC (Tauri Commands & Events)
                                ▼
┌────────────────────────────────────────────────────────────────────────┐
│                   Camada 2: Engine Nativo Rust                        │
│                     (`crates/travelcut-media`)                         │
│                                                                        │
│  ┌──────────────────┐  ┌──────────────────┐  ┌───────────────────────┐ │
│  │  FFprobe Parser  │  │ Bounded Job Queue│  │ Hardware Transcode    │ │
│  │ (Streams, HDR,   │  │  (Prioridades,   │  │ (NVENC, QuickSync,    │ │
│  │   Rotacao, FPS)  │  │   Cancelamento)  │  │  VideoToolbox, VAAPI) │ │
│  └──────────────────┘  └──────────────────┘  └───────────────────────┘ │
│  ┌──────────────────┐  ┌──────────────────┐  ┌───────────────────────┐ │
│  │ Thumbnail Engine │  │ Waveform Engine  │  │ SHA-256 Cache Manager │ │
│  │ (Frames, Grades) │  │  (Min/Max Peaks) │  │(Invalidação por hash) │ │
│  └──────────────────┘  └──────────────────┘  └───────────────────────┘ │
└───────────────────────────────┬────────────────────────────────────────┘
                                │ IPC / CLI Pipeline (WAV 16kHz & JSON)
                                ▼
┌────────────────────────────────────────────────────────────────────────┐
│                 Camada 3: Motor de IA e Pesquisa                       │
│                     (`research/transcription`)                         │
│                                                                        │
│  ┌──────────────────┐  ┌──────────────────┐  ┌───────────────────────┐ │
│  │ Voice Activity   │  │ ASR Engine       │  │ Word Timestamp        │ │
│  │ Detection (VAD)  │  │ (faster-whisper  │  │ Aligner (DTW &        │ │
│  │ (Fluxo Espectral)│  │  & Mock Engine)  │  │  Hesitacoes)          │ │
│  └──────────────────┘  └──────────────────┘  └───────────────────────┘ │
│  ┌──────────────────┐  ┌──────────────────┐  ┌───────────────────────┐ │
│  │ Silence Detector │  │ Context Protector│  │ Phase 4 Cut Preparer  │ │
│  │(Taxonomia Viagem)│  │ (Drone/Scenery)  │  │(Sugestoes de corte)   │ │
│  └──────────────────┘  └──────────────────┘  └───────────────────────┘ │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Responsabilidades por Camada

### 2.1 Camada 1: Desktop UI (Frontend)
- **Localizacao**: `src/`
- **Tecnologias**: React 18, TypeScript, Tailwind CSS, Lucide React, Context API.
- **Responsabilidade**:
  - Apresentacao visual completa em tema dark estilo DaVinci Resolve.
  - Navegacao fluida entre abas principais: Home, Projeto (Biblioteca de Midia), Edicao (Cortes IA e Revisao), Narrativa e Exportacao.
  - Sincronizacao de reproducao de video com timecode e marcadores de palavras transcritas.
  - Interface de edicao de video baseada em texto: selecao de trechos, descarte de hesitacoes vocais (*"eh"*, *"hum"*), aprovacao/rejeicao de cortes.
  - Geracao e download de arquivos FCPXML 1.8 e EDL para DaVinci Resolve.

### 2.2 Camada 2: Engine Nativo Rust
- **Localizacao**: `crates/travelcut-media/`
- **Tecnologias**: Rust 2021, Tokio (async), Rayon (paralelismo CPU), SHA-2 (hashing), Tracing.
- **Responsabilidade**:
  - Inspecao e inventario de arquivos de midia em disco, filtrando arquivos de sistema temporarios.
  - Sondagem profunda via FFprobe: resolucao, framerate racional, canais e taxa de amostragem de audio, rotacoes de camera (smartphones/drones), perfis HDR e espacos de cor.
  - Geracao paralela e eficiente de thumbnails e filmstrips (grades de visualizacao para a timeline).
  - Extracao e decodificacao de picos de audio para calculo de waveforms de alta resolucao.
  - Transcodificacao de proxies de video (720p/1080p ProRes ou H.264) com deteccao e selecao automatica de aceladores de hardware disponiveis na maquina host (NVIDIA NVENC, Intel QuickSync, Apple VideoToolbox, Linux VAAPI).
  - Gerenciamento de fila de tarefas com controle de concorrencia limitada (bounded concurrency), permitindo priorizacao de tarefas interativas sobre tarefas de fundo e suporte total a cancelamento imediato.
  - Cache deterministico em disco indexado pelo SHA-256 do arquivo original, garantindo que alteracoes ou repeticoes nao recalculem midias ja processadas.

### 2.3 Camada 3: Motor de IA e Pesquisa de Transcricao
- **Localizacao**: `research/transcription/`
- **Tecnologias**: Python 3.13, faster-whisper (CTranslate2), NumPy.
- **Responsabilidade**:
  - Deteccao de Atividade Vocal (VAD) baseada em energia de quadro e fluxo espectral (spectral flux), com piso de ruido adaptativo.
  - Classificacao de pausas segundo a taxonomia exclusiva de videos de viagem:
    - Pausas curtas de fala (< 400ms): preservadas para naturalidade.
    - Pausas medias de frase (400ms a 1200ms): mantidas como respiracao estrutural.
    - Silencios mortos removiveis (>= 1200ms): sugeridos para corte automatico.
    - Trechos com metadado cenico (drone, cachoeira, estrada): protegidos de corte para manter o impacto contemplativo da viagem.
  - Reconhecimento de fala multilingue com enfase em Portugues (pt-BR).
  - Alinhamento temporal fino palavra por palavra (Word Alignment) utilizando Dynamic Time Warping (DTW) e matrizes de confianca acustica.
  - Identificacao e classificacao de marcadores de hesitacao vocal (*"eh"*, *"hum"*, *"tipo assim"*, *"ne"*).
  - Conversao direta dos resultados em contratos compativeis com a Fase 4 (`CutSuggestion`, `TranscriptionDocument`).

---

## 3. Fluxo de Dados Ponta a Ponta

1. **Ingestao de Midias**:
   - O usuario aponta uma pasta contendo gravacoes brutas de cameras (Sony FX3, DJI Mini 4 Pro, GoPro Hero 12, smartphones).
   - O scanner de inventario em Rust varre o diretorio, identifica tipos de midia aceitos e executa o FFprobe para obter metadados tecnicos completos.

2. **Pre-processamento em Segundo Plano**:
   - A fila de tarefas em Rust inicia simultaneamente a extracao de audio (WAV 16kHz mono), a geracao de miniaturas (thumbnails) e a construcao de formas de onda (waveforms).
   - O cache SHA-256 garante que arquivos ja inspecionados sejam carregados instantaneamente da memoria persistente.

3. **Transcricao e Deteccao de Cortes IA**:
   - O audio WAV 16kHz normalizado e processado pelo modulo de transcricao e VAD.
   - O motor Whisper gera o texto bruto e os timestamps de segmentos.
   - O alinhador de palavras correlaciona a energia acustica com cada token, detectando hesitacoes e silencios removiveis.
   - O algoritmo contextual analisa dicas de cena (ex.: tags de drone ou audio ambiental ruidoso) e emite a lista final de `CutSuggestion`.

4. **Revisao e Edicao no Desktop**:
   - A interface do TravelCut Studio apresenta a transcricao sincronizada com o reprodutor de video e a timeline NLE.
   - O editor revisa os cortes sugeridos: aceita, rejeita ou seleciona novos trechos de texto para cortar manualmente.
   - A timeline reflete em tempo real os trechos aceitos e rejeitados, calculando o tempo economizado no video final.

5. **Exportacao para Softwares Profissionais**:
   - O usuario seleciona o formato de saida desejado (FCPXML 1.8 para DaVinci Resolve ou EDL tradicional).
   - O exportador sintetiza a linha de tempo de edicao (timeline), agrupando os trechos mantidos e inserindo marcadores coloridos de revisao de IA, prontos para colorizacao e finalizacao de audio.
