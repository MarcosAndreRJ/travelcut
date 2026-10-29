/**
 * Contrato com a Fase 2 (HomeView)
 *
 * Regra: Não importar de src/features/home/ nesta branch.
 * Este módulo define os tipos que a Fase 2 exportará para permitir que
 * a integração ocorra de forma transparente no momento do merge.
 */

export interface HomeProject {
  id: string;
  name: string;
  path: string;
  lastModified: string;
  clipCount: number;
  totalDurationFormatted?: string;
  thumbnailUrl?: string;
}

export type HomeAction =
  | { type: 'open_project'; projectId: string }
  | { type: 'new_project' }
  | { type: 'import_media' }
  | { type: 'quick_tour' };

export interface HomeViewProps {
  project: HomeProject | null;
  onAction: (action: HomeAction) => void;
}
