import { describe, expect, it } from 'vitest';
import {
  afterModeSkipTarget,
  clampToWindow,
  contextWindow,
  formatSourceSeconds,
  mediaElementErrorText,
  MEDIA_ERR_DECODE,
  MEDIA_ERR_NETWORK,
  MEDIA_ERR_SRC_NOT_SUPPORTED
} from '../mediaTime';

describe('mediaTime: janela de reprodução', () => {
  it('adiciona contexto antes e depois do intervalo', () => {
    expect(contextWindow(10, 12, 2, 100)).toEqual({ start: 8, end: 14 });
  });

  it('o início nunca fica negativo', () => {
    expect(contextWindow(1, 5, 2, null)).toEqual({ start: 0, end: 7 });
  });

  it('o fim é limitado pela duração conhecida', () => {
    expect(contextWindow(50, 60, 2, 55)).toEqual({ start: 48, end: 55 });
  });

  it('sem duração conhecida, o fim não é limitado', () => {
    expect(contextWindow(50, 60, 2, null)).toEqual({ start: 48, end: 62 });
    expect(contextWindow(50, 60, 2, 0)).toEqual({ start: 48, end: 62 });
  });

  it('início além da duração colapsa na duração (janela vazia, sem NaN)', () => {
    expect(contextWindow(200, 210, 0, 100)).toEqual({ start: 100, end: 100 });
  });

  it('entradas não finitas viram valores neutros', () => {
    expect(contextWindow(Number.NaN, Number.NaN, Number.NaN, null)).toEqual({ start: 0, end: 0 });
  });

  it('fim menor que o início é tratado como intervalo vazio', () => {
    expect(contextWindow(10, 5, 0, null)).toEqual({ start: 10, end: 10 });
  });
});

describe('mediaTime: clamp', () => {
  const window = { start: 8, end: 14 };

  it('valor dentro da janela é mantido', () => {
    expect(clampToWindow(10, window)).toBe(10);
  });

  it('valor fora é limitado às bordas', () => {
    expect(clampToWindow(5, window)).toBe(8);
    expect(clampToWindow(20, window)).toBe(14);
  });

  it('NaN volta para o início da janela', () => {
    expect(clampToWindow(Number.NaN, window)).toBe(8);
  });
});

describe('mediaTime: salto do modo "após o corte"', () => {
  it('dentro de [início, fim) salta para o fim do trecho removido', () => {
    expect(afterModeSkipTarget(5, 4, 6)).toBe(6);
    expect(afterModeSkipTarget(4, 4, 6)).toBe(6);
  });

  it('o fim do trecho é exclusivo: exatamente no fim não salta', () => {
    expect(afterModeSkipTarget(6, 4, 6)).toBeNull();
  });

  it('antes ou depois do trecho não salta', () => {
    expect(afterModeSkipTarget(3, 4, 6)).toBeNull();
    expect(afterModeSkipTarget(7, 4, 6)).toBeNull();
  });

  it('intervalo inválido (fim não posterior ao início) não salta', () => {
    expect(afterModeSkipTarget(5, 6, 6)).toBeNull();
    expect(afterModeSkipTarget(5, 6, 4)).toBeNull();
  });

  it('valores não finitos não saltam', () => {
    expect(afterModeSkipTarget(Number.NaN, 4, 6)).toBeNull();
    expect(afterModeSkipTarget(5, Number.NaN, 6)).toBeNull();
  });
});

describe('mediaTime: formatação e erros do elemento', () => {
  it('segundos da fonte com três casas e vírgula', () => {
    expect(formatSourceSeconds(12.3456)).toBe('12,346 s');
    expect(formatSourceSeconds(0)).toBe('0,000 s');
    expect(formatSourceSeconds(-3)).toBe('0,000 s');
    expect(formatSourceSeconds(Number.NaN)).toBe('—');
  });

  it('códigos de MediaError têm texto PT-BR e nunca inventam causa', () => {
    expect(MEDIA_ERR_NETWORK).toBe(2);
    expect(MEDIA_ERR_DECODE).toBe(3);
    expect(MEDIA_ERR_SRC_NOT_SUPPORTED).toBe(4);
    expect(mediaElementErrorText(MEDIA_ERR_SRC_NOT_SUPPORTED)).toContain('não suportado');
    expect(mediaElementErrorText(MEDIA_ERR_DECODE)).toBe('erro ao decodificar o vídeo');
    expect(mediaElementErrorText(MEDIA_ERR_NETWORK)).toBe('erro de leitura do arquivo pelo host de mídia');
    expect(mediaElementErrorText(null)).toBe('erro desconhecido ao carregar o vídeo');
  });
});
