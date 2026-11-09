import { describe, expect, it } from 'vitest';
import { afterModeSkipTargets, removedIntervalsFromPlan } from '../../media/mediaTime';
import { afterLabelOf } from '../BeforeAfterCompare';

const clip = (mediaId: string, inS: number, outS: number) => ({
  mediaId,
  sourceInUs: inS * 1_000_000,
  sourceOutUs: outS * 1_000_000
});

describe('removedIntervalsFromPlan (E08)', () => {
  it('deriva todos os trechos removidos da mídia com dois cortes aplicados', () => {
    // Plano mantém [10,20) e [30,40) da mídia m1; outra mídia não entra.
    const clips = [clip('m1', 30, 40), clip('m1', 10, 20), clip('m2', 0, 5)];
    const removed = removedIntervalsFromPlan(clips, 'm1');
    expect(removed.slice(0, 2)).toEqual([
      { start: 0, end: 10 },
      { start: 20, end: 30 }
    ]);
    // Último trecho vai até o fim da mídia (valor finito grande, o elemento limita ao duration).
    expect(removed[2].start).toBe(40);
    expect(removed[2].end).toBeGreaterThan(1e6);
  });

  it('pula cada trecho removido, não só o do candidato aberto', () => {
    const removed = removedIntervalsFromPlan([clip('m1', 10, 20), clip('m1', 30, 40)], 'm1');
    // Dentro do primeiro trecho removido (0–10): salta para 10.
    expect(afterModeSkipTargets(5, removed)).toBe(10);
    // Dentro do segundo trecho removido (20–30): salta para 30, sem tocar o candidato aberto.
    expect(afterModeSkipTargets(25, removed)).toBe(30);
    // Dentro de trecho mantido: nada a pular.
    expect(afterModeSkipTargets(15, removed)).toBeNull();
  });

  it('mídia sem trechos no plano fica inteiramente fora do Depois', () => {
    const removed = removedIntervalsFromPlan([clip('m1', 10, 20)], 'm9');
    expect(removed).toEqual([{ start: 0, end: expect.any(Number) }]);
    expect(afterModeSkipTargets(3, removed)).not.toBeNull();
  });

  it('ignora trechos inválidos (saída antes da entrada) e valores não finitos', () => {
    expect(afterModeSkipTargets(Number.NaN, [{ start: 0, end: 5 }])).toBeNull();
    expect(afterModeSkipTargets(2, [{ start: 5, end: 1 }, { start: 0, end: 5 }])).toBe(5);
  });
});

describe('rótulo do Depois (E08)', () => {
  it('sem plano rotula "Depois (só este corte)"; com plano, "Depois (plano vigente)"', () => {
    expect(afterLabelOf('candidate')).toBe('Depois (só este corte)');
    expect(afterLabelOf('plan')).toBe('Depois (plano vigente)');
  });
});
