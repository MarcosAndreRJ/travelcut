import { describe, expect, it } from 'vitest';
import type { EditSuggestion, MediaAssetDto } from '../../../types/ipc';
import { resolveFocusAsset, suggestionFocusOf } from '../suggestionFocus';

const sug = (over: Partial<EditSuggestion> = {}): EditSuggestion =>
  ({
    suggestionId: 's1',
    mediaId: 'm1',
    kind: 'silence',
    status: 'Proposed',
    startSeconds: 10,
    endSeconds: 14,
    ...over
  }) as EditSuggestion;

const asset = (mediaId: string): MediaAssetDto => ({ mediaId, relativePath: `1-Bruto/${mediaId}.MP4` }) as MediaAssetDto;

describe('suggestionFocusOf', () => {
  it('usa o início do intervalo efetivo (ajustado) como ponto de posicionamento', () => {
    const s = sug({ effectiveStartSeconds: 11.5, effectiveEndSeconds: 13 });
    expect(suggestionFocusOf([s], 's1', 3)).toEqual({ suggestionId: 's1', mediaId: 'm1', startSeconds: 11.5, nonce: 3 });
  });

  it('cai no intervalo original quando não há ajuste', () => {
    expect(suggestionFocusOf([sug()], 's1', 1).startSeconds).toBe(10);
  });

  it('sem sugestão conhecida mantém só o id e o nonce', () => {
    expect(suggestionFocusOf([], 'desconhecida', 2)).toEqual({ suggestionId: 'desconhecida', nonce: 2 });
  });
});

describe('resolveFocusAsset', () => {
  const focus = { mediaId: 'm1', suggestionId: 's1', startSeconds: 10, nonce: 1 };

  it('aguarda enquanto a lista de mídias ainda não chegou', () => {
    expect(resolveFocusAsset(focus, [])).toEqual({ kind: 'pending' });
  });

  it('seleciona o clipe do candidato quando a mídia existe', () => {
    expect(resolveFocusAsset(focus, [asset('m2'), asset('m1')])).toEqual({ kind: 'select', asset: asset('m1') });
  });

  it('encerra sem seleção quando a mídia não está mais na biblioteca', () => {
    expect(resolveFocusAsset(focus, [asset('m2')])).toEqual({ kind: 'done' });
  });

  it('encerra quando o foco não tem mídia', () => {
    expect(resolveFocusAsset({ nonce: 1 }, [asset('m1')])).toEqual({ kind: 'done' });
  });
});
