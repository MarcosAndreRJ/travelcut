# Documentacao Oficial do TravelCut Studio

Bem-vindo a documentacao central do **TravelCut Studio**, um editor de video desktop profissional (NLE assistido por IA) especializado em fluxos de trabalho para vlogs de viagem, filmagens externas e producoes multicamara.

O objetivo do TravelCut Studio e eliminar o trabalho repetitivo de decupagem inicial, selecao de melhores takes, remocao de silencios vazios e hesitacoes vocais, preservando pausas ambientais cenicas (drones, paisagens, cachoeiras) e gerando cortes prontos para exportacao direta para DaVinci Resolve e Final Cut Pro.

---

## Indice da Documentacao

A documentacao esta estruturada em modulos tematicos e tecnicos detalhados:

1. [Arquitetura Geral do Sistema](./ARCHITECTURE.md)
   - Diagrama arquitetural das camadas Desktop, Engine Rust e Pesquisa Python.
   - Fluxo de dados ponta a ponta: da ingestao de midia bruta a exportacao NLE.
   - Modelo de concorrencia e comunicacao entre processos.

2. [Modulos, Fases e Funcionalidades](./MODULES_AND_FEATURES.md)
   - Detalhamento completo de cada fase implementada no projeto:
     - **Fase 1**: Design System NLE & AppShell.
     - **Fase 2**: Home Dashboard & Gestao de Projetos.
     - **Fase 3**: Biblioteca de Midias & Transcricao Whisper.
     - **Fase 4**: Workspace de Revisao, Edicao Baseada em Texto & Exportador DaVinci.
     - **Fase 6**: Engine de Midia em Rust de Alta Performance (`travelcut-media`).
     - **Fase 7**: Pesquisa de Transcricao, VAD & Alinhamento Temporal Fino (`research/transcription`).

3. [Contratos de Dados e Integracao](./DATA_CONTRACTS.md)
   - Especificacao formal das estruturas de dados compartilhadas entre TypeScript, Rust e Python:
     - `MediaAsset` e metadados tecnicos de camera/codec.
     - `TranscriptionDocument`, `TranscriptSegment` e `TranscriptWord`.
     - `CutSuggestion`, motivos de corte e preservacao contextual.
     - `WaveformData`, `ThumbnailOptions` e `TranscodeJob`.

4. [Guia de Desenvolvimento e Execucao](./GETTING_STARTED.md)
   - Pre-requisitos (Node.js, Rust/Cargo, Python, FFmpeg).
   - Como rodar o frontend React/TypeScript.
   - Como compilar e testar o engine Rust (`travelcut-media`).
   - Como executar o pipeline e benchmarks de pesquisa em Python.

5. [Guias Especificos de Integracao](./)
   - [Guia de Integracao da Fase 1](./PHASE_01_INTEGRATION_GUIDE.md)
   - [Guia de Integracao da Fase 3](./PHASE_03_INTEGRATION_GUIDE.md)
   - [Relatorio de Pesquisa da Fase 7 (Transcricao & VAD)](./PHASE_07_TRANSCRIPTION_RESEARCH.md)

---

## Status do Projeto e Fases Concluidas

| Fase | Escopo | Stack Tecnologica | Status | Cobertura de Testes |
|---|---|---|---|---|
| **Fase 1** | Design System NLE & AppShell | React 18, TypeScript, Tailwind CSS, Lucide | Concluido | Compilacao estrita |
| **Fase 2** | Home Dashboard & Templates | React 18, TypeScript, Tailwind CSS | Concluido | Validado em visualizacao isolada |
| **Fase 3** | Biblioteca de Midias & Transcricao | React 18, TypeScript, CSS customizado NLE | Concluido | Tipagem e contratos integrados |
| **Fase 4** | Review Workspace, Edicao & DaVinci | React 18, TypeScript, Context API | Concluido | Exportador FCPXML/EDL testado |
| **Fase 6** | Engine de Midia em Rust | Rust (Tokio, Rayon, SHA-256, FFmpeg/FFprobe) | Concluido | 23 testes automatizados (100% pass) |
| **Fase 7** | Transcricao, VAD & Word Alignment | Python 3.13, faster-whisper, NumPy | Concluido | 43 testes automatizados (100% pass) |

---

## Principais Diferenciais Tecnologicos

1. **Taxonomia de Silencios Contextual para Viagens**:
   - Diferenciacao algoritmica entre pausas naturais de respiracao (<400ms), pausas de frase (400ms-1200ms) e silencios mortos removiveis (>=1200ms).
   - Preservacao automatica de cenas contemplativas (drones, cachoeiras, transicoes de estrada), impedindo que algoritmos cegos de ASR destruam o ritmo do video.

2. **Engine Rust Bounded & Hardware-Accelerated**:
   - Extracao de metadados FFprobe com fallback robusto.
   - Geracao de proxies 720p/1080p com deteccao e selecao automatica de aceleracao por hardware (NVENC, Intel QuickSync, Apple VideoToolbox, VAAPI) e fallback x264/ProRes.
   - Geracao de formas de onda (waveforms) multirresolucao em picos min/max normalizados.
   - Cache deterministico com hashing SHA-256 de caminho + tamanho + data de modificacao.

3. **Edicao Baseada em Texto com Sincronizacao Bidirecional**:
   - Edicao do video diretamente pela leitura do texto transcrito.
   - Ao riscar palavras ou aceitar cortes recomendados, a timeline atualiza os pontos de In/Out e permite visualizacao imediata no player.
   - Exportacao direta para DaVinci Resolve via FCPXML 1.8 e marcadores de edicao coloridos.
