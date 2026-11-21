import type { EditSuggestion, MediaAssetDto } from '../../types/ipc';
import { effectiveInterval } from './reviewModel';

/**
 * Pedido de foco vindo de fora da Biblioteca (ex.: painel inicial ou atalho de sugestão).
 * A Biblioteca abre a aba Sugestões, seleciona o clipe do candidato e posiciona no início do trecho.
 */
export interface SuggestionFocus {
  mediaId?: string;
  suggestionId?: string;
  /** Início do trecho efetivo (ajustado ou original), em segundos. */
  startSeconds?: number;
  /** Muda a cada pedido, mesmo que o alvo seja o mesmo. */
  nonce: number;
}

/** Monta o foco a partir das sugestões carregadas. Sem a sugestão, mantém só o id e o nonce. */
export const suggestionFocusOf = (
  suggestions: EditSuggestion[],
  suggestionId: string | undefined,
  nonce: number
): SuggestionFocus => {
  const found = suggestionId ? suggestions.find((s) => s.suggestionId === suggestionId) : undefined;
  if (!found) return { suggestionId, nonce };
  return {
    suggestionId: found.suggestionId,
    mediaId: found.mediaId,
    startSeconds: effectiveInterval(found).start,
    nonce
  };
};

export type FocusResolution =
  | { kind: 'pending' }
  | { kind: 'done' }
  | { kind: 'select'; asset: MediaAssetDto };

/**
 * Resolve o clipe do foco. 'pending' enquanto a lista da Biblioteca ainda não chegou;
 * 'done' quando não há clipe a selecionar (sem mediaId ou mídia fora da biblioteca).
 */
export const resolveFocusAsset = (focus: SuggestionFocus, assets: MediaAssetDto[]): FocusResolution => {
  if (!focus.mediaId) return { kind: 'done' };
  const asset = assets.find((a) => a.mediaId === focus.mediaId);
  if (asset) return { kind: 'select', asset };
  return assets.length === 0 ? { kind: 'pending' } : { kind: 'done' };
};
