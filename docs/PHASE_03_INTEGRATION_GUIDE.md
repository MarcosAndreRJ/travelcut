# Guia de Integracao da Fase 3: Biblioteca de Midias e Transcricao

Este documento estabelece o contrato formal de integracao, arquitetura de componentes e eventos entregues na **Fase 3 (Biblioteca de Midias e Transcricao)** para consumo direto pela **Fase 4 (Analise e Cortes IA)** e demais modulos do **TravelCut Studio**.

---

## 1. Visao Geral da Arquitetura Entregue

A Fase 3 disponibiliza o workspace completo de ingestao, organizacao de metadados, inspecao de transcricao assistida por IA e gestao de cortes recomendados:

- **Workspace Principal**: `src/features/library/MediaLibraryWorkspace.tsx` plugado diretamente na aba `'project'` do `src/App.tsx`.
- **Componentes Estruturais**:
  - `ProjectHeader`: Edicao inline do titulo, metadados de resolucao/codec e stepper de progresso de 5 etapas.
  - `ProjectStatsCards`: 4 cards com metricas consolidadas (arquivos, duracao total, espaco em disco, cortes pendentes).
  - `MediaGrid`: Visualizacao em grade ou lista de clipes de video com badges de camera (Sony FX3, DJI Mini 4 Pro, GoPro Hero 12), resolucao, framerate, status de transcricao e menu contextual.
  - `ProcessingQueue`: Painel recolhivel de tarefas de processamento de audio/video e transcricao Whisper com barra de progresso, status e acao de reexecucao.
  - `VideoPlayerPreview`: Player de video integrado com timecode, controles de transporte (play/pause, seek -5s/+5s, barra de progresso) e metadados de camera/audio.
  - `TranscriptInspector`: Inspetor com abas ('transcription', 'cuts', 'keywords'), sincronizacao com timecode do player, busca de texto, destaques de hesitacoes/silencios e botoes de aceitar/descartar corte.
  - `ProjectSummaryCard`: Resumo executivo com contadores de cortes automaticos e revisao, tempo total economizado e botao "Continuar para Edicao".
- **Contratos & Tipagens**:
  - `src/features/library/types.ts`: Tipagem dos componentes da biblioteca e dados mock de teste (Roraima Expedition).
  - `src/features/media/types.ts`: Contrato de integracao formal com a Fase 4 (`MediaAsset`, `TranscriptWord`, `TranscriptSegment`, `CutSuggestion`, barramentos de eventos tipados).
  - `src/features/media/styles/media.css`: Estilizacao especifica de scrollbars escuras NLE, glow de selecao e marcacoes de palavras ativas.

---

## 2. Contrato de Tipos para a Fase 4 (`src/features/media/types.ts`)

Os seguintes modelos de dados foram projetados para desacoplamento e consumo direto:

```typescript
export interface MediaAsset {
  id: string;
  name: string;
  path: string;
  durationSec: number;
  durationFormatted: string;
  fileSizeBytes: number;
  fileSizeFormatted: string;
  codec: string;
  resolution: string;
  fps: number;
  aspectRatio: '16:9' | '9:16' | '4:3' | '1:1';
  cameraModel?: string;
  cameraMake?: string;
  recordedAt?: string;
  thumbnailUrl?: string;
  audioChannels: number;
  sampleRateHz: number;
  status: 'ready' | 'processing' | 'error' | 'unprocessed';
  processingProgress?: number;
  hasAudio: boolean;
  transcriptionStatus: 'none' | 'transcribing' | 'completed' | 'failed';
  language?: string;
  confidenceScore?: number;
}

export interface TranscriptWord {
  id: string;
  word: string;
  startSec: number;
  endSec: number;
  confidence: number;
  speakerTag?: string;
  isHesitation?: boolean;
  isSilence?: boolean;
}

export interface CutSuggestion {
  id: string;
  assetId: string;
  type: 'hesitation' | 'silence' | 'redundancy' | 'bad_take' | 'technical_glitch';
  label: string;
  reason: string;
  startSec: number;
  endSec: number;
  durationSec: number;
  confidence: number;
  status: 'suggested' | 'accepted' | 'rejected' | 'applied';
}
```

---

## 3. Integracao no `src/App.tsx`

O workspace esta acoplado a navegacao principal do NLE:

```tsx
import { MediaLibraryWorkspace } from './features/library';
import './features/media';

// Dentro do AppShell:
{activeTab === 'project' && (
  <MediaLibraryWorkspace
    onNavigateTab={(tab) => {
      if (tab === 'narrative') {
        setActiveTab('edit');
      } else {
        setActiveTab(tab as NavigationTab);
      }
    }}
  />
)}
```

---

## 4. Validacao de Build

A compilacao TypeScript (`tsc -b`) e o empacotamento Vite (`vite build`) foram executados com sucesso gerando bundle de producao sem erros.
