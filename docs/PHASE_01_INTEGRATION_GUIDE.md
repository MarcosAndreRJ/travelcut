# Guia de Integracao da Fase 1: Design System & AppShell

Este documento estabelece o contrato formal de integracao e a arquitetura visual entregue na **Fase 1** para consumo direto pela **Fase 2 (Home)** e pelas telas subsequentes do **TravelCut Studio**.

---

## 1. Visao Geral da Arquitetura Entregue

A Fase 1 entrega a infraestrutura de UI, navegacao global e tokens do TravelCut Studio:
- **Design Tokens**: Configurados em `tailwind.config.js` com paleta dark para software NLE profissional de desktop.
- **Componentes Base Reutilizaveis**: Localizados em `src/components/ui/` e exportados via `src/components/ui/index.ts`.
- **AppShell Global**: `src/components/layout/AppShell.tsx` contendo Sidebar persistente com recolhimento, Top Contextual Bar com acoes rapidas, Viewport principal com rolagem independente e Slot inferior para a Timeline NLE.
- **Contratos da Fase 2**: `src/types/homeContract.ts` e `src/app/placeholders/HomePlaceholderView.tsx`.
- **Placeholders**: `HomePlaceholderView`, `ProjectPlaceholderView` e `ReviewPlaceholderView` em `src/app/placeholders/`.

---

## 2. Design Tokens (Tailwind + CSS)

### Paleta Dark NLE (`studio.*`):
| Token | Hex | Finalidade |
|---|---|---|
| `studio-bg` | `#080b11` | Fundo principal da aplicacao |
| `studio-panel` | `#0d121c` | Fundo de paineis, gavetas e headers |
| `studio-sidebar` | `#0a0e17` | Fundo da barra lateral persistente |
| `studio-card` | `#121824` | Fundo de cards de conteudo |
| `studio-cardHover` | `#182030` | Hover de cards e itens de lista |
| `studio-cardActive` | `#1f293d` | Estado ativo/selecionado de cards |
| `studio-border` | `#1e2638` | Bordas e divisores estruturais |
| `studio-borderLight` | `#2b364e` | Bordas de cards e botoes secundarios |
| `studio-borderFocus` | `#3b82f6` | Anel de foco acessivel |
| `studio-primary` | `#2563eb` | Azul DaVinci Resolve para acoes principais |
| `studio-primaryHover` | `#1d4ed8` | Hover da acao primaria |
| `studio-turquoise` | `#06b6d4` | Destaque IA, transcricoes e status ativo |
| `studio-success` | `#10b981` | Cortes aceitos, processamento 100% |
| `studio-warning` | `#f59e0b` | Avisos de corte, atencao de audio |
| `studio-error` | `#ef4444` | Falhas de codec, clipes ausentes |
| `studio-text` | `#f8fafc` | Texto principal com alto contraste |
| `studio-muted` | `#94a3b8` | Texto secundario e rotulos |
| `studio-subtle` | `#64748b` | Metadados, timecodes e icones inativos |

### Tipografia:
- **Mono**: `JetBrains Mono`, `Fira Code`, `monospace` (timecodes, taxas de amostragem, resolucoes).
- **Sans**: `Plus Jakarta Sans`, `Inter`, `sans-serif` (titulos, navegacao, botoes).

---

## 3. Componentes do Design System (`src/components/ui/`)

Todos os componentes sao tipados com TypeScript e prontos para uso:

1. **`Button`**:
   - Variantes: `'primary'`, `'secondary'`, `'ghost'`, `'danger'`, `'accent'`.
   - Tamanhos: `'xs'`, `'sm'`, `'md'`, `'lg'`.
   - Suporte a `icon`, `iconPosition`, `loading`, `fullWidth`.
2. **`Card`**:
   - `Card`, `CardHeader`, `CardTitle`, `CardDescription`, `CardContent`, `CardFooter`.
   - Prop `interactive` para cards clicaveis com feedback de elevacao e borda.
3. **`Badge`**:
   - Variantes: `'primary'`, `'turquoise'`, `'success'`, `'warning'`, `'error'`, `'neutral'`, `'purple'`.
   - Suporte a `dot` (indicador LED iluminado).
4. **`ProgressBar`**:
   - Indicadores de progresso linear com variantes de cor, rotulo, percentual e estilizacao NLE.
5. **`PageHeader`**:
   - Titulo principal, subtitulo, badge opcional e slot `actions` para botoes de topo.
6. **`EmptyState`**:
   - Estado vazio profissional para listas vazias, cartao SD ausente ou sem midias.

---

## 4. Contrato de Integracao para a Fase 2 (Home)

### Tipos Compartilhados (`src/types/homeContract.ts`):
```typescript
export interface HomeProject {
  id: string;
  name: string;
  path: string;
  lastModified: string;
  clipCount: number;
  totalDurationFormatted: string;
  thumbnailUrl?: string;
  cameraInfo?: string;
}

export type HomeActionType = 
  | 'open_project' 
  | 'import_media' 
  | 'start_analysis' 
  | 'export_project' 
  | 'quick_tour';

export interface HomeAction {
  type: HomeActionType;
  projectId?: string;
  payload?: any;
}

export interface HomeViewProps {
  project?: HomeProject | null;
  onAction?: (action: HomeAction) => void;
  onNavigateToTab?: (tab: 'home' | 'project' | 'analysis' | 'edit' | 'review' | 'export' | 'settings') => void;
}
```

### Como a Fase 2 pluga seu componente no `App.tsx`:
Substitua apenas o bloco condicional da aba `'home'` no `src/App.tsx`:

```tsx
// 1. Importe sua view final implementada na Fase 2:
import { HomeView } from './features/home/HomeView';

// 2. No corpo do AppShell em src/App.tsx:
{activeTab === 'home' && (
  <HomeView
    project={activeProject}
    onAction={(action) => handleHomeAction(action)}
    onNavigateToTab={(tab) => setActiveTab(tab)}
  />
)}
```

---

## 5. Garantia de Build e Isolamento

- O AppShell gerencia a navegacao (`activeTab`, `onTabChange`), os menus laterais com tooltips acessiveis, e o slot da Timeline NLE.
- A tela Home nao deve reimplementar Sidebar nem o Header global, focando exclusivamente no conteudo de gestao de projetos, ingestao rapida e metricas.
