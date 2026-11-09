import { describe, expect, it } from 'vitest';
import { keptSegmentAt, keptSegmentsFromPlan } from '../mediaTime';

const clip = (
  mediaId: string,
  inS: number,
  outS: number,
  extra: { speedNum?: number; speedDen?: number; zoom?: number | null } = {}
) => ({
  mediaId,
  sourceInUs: inS * 1_000_000,
  sourceOutUs: outS * 1_000_000,
  speedNum: extra.speedNum ?? 1,
  speedDen: extra.speedDen ?? 1,
  zoom: extra.zoom
});

describe('keptSegmentsFromPlan (F14.8 item 2)', () => {
  it('dois cortes: mantém [10,20) e [30,40) em ordem, sem remover o outro media', () => {
    const segs = keptSegmentsFromPlan([clip('m1', 30, 40), clip('m2', 0, 5), clip('m1', 10, 20)], 'm1');
    expect(segs).toEqual([
      { start: 10, end: 20, rate: 1, zoom: null },
      { start: 30, end: 40, rate: 1, zoom: null }
    ]);
  });

  it('velocidade 2x e zoom do clipe são carregados por segmento', () => {
    const segs = keptSegmentsFromPlan(
      [clip('m1', 10, 20), clip('m1', 30, 40, { speedNum: 2, speedDen: 1, zoom: 1.2 })],
      'm1'
    );
    expect(segs[0]).toEqual({ start: 10, end: 20, rate: 1, zoom: null });
    expect(segs[1]).toEqual({ start: 30, end: 40, rate: 2, zoom: 1.2 });
  });

  it('taxa inválida vira 1 e zoom não maior que 1 vira null', () => {
    const segs = keptSegmentsFromPlan(
      [clip('m1', 0, 5, { speedNum: 0, speedDen: 1, zoom: 0.9 }), clip('m1', 6, 9, { zoom: Number.NaN })],
      'm1'
    );
    expect(segs[0].rate).toBe(1);
    expect(segs[0].zoom).toBeNull();
    expect(segs[1].zoom).toBeNull();
  });

  it('segmento com saída não maior que entrada é ignorado', () => {
    expect(keptSegmentsFromPlan([clip('m1', 5, 5)], 'm1')).toEqual([]);
  });
});

describe('keptSegmentAt (F14.8 item 2)', () => {
  const segs = keptSegmentsFromPlan(
    [clip('m1', 10, 20), clip('m1', 30, 40, { speedNum: 2, speedDen: 1, zoom: 1.2 })],
    'm1'
  );

  it('retorna o trecho mantido que contém o instante, com a velocidade 2x do segundo corte', () => {
    expect(keptSegmentAt(35, segs)).toEqual({ start: 30, end: 40, rate: 2, zoom: 1.2 });
    expect(keptSegmentAt(12, segs)?.rate).toBe(1);
  });

  it('instante em trecho removido, fora da mídia ou não finito devolve null', () => {
    expect(keptSegmentAt(25, segs)).toBeNull();
    expect(keptSegmentAt(40, segs)).toBeNull();
    expect(keptSegmentAt(Number.NaN, segs)).toBeNull();
  });
});
