import { describe, expect, it } from 'vitest';
import {
  ZOOM_SCALE_OPTIONS,
  isSupportedZoomScale,
  resolveZoomWindows,
  validateZoomInput,
  zoomPreviewSeekSeconds,
  zoomScaleAtSource,
  zoomTransformAtSource,
  zoomWindowsFromPlan,
  zoomWindowsFromSuggestions
} from '../zoomWindows';
import type { EditPlanDto, EditSuggestion } from '../../../types/ipc';

const zoom = { startSec: 10, endSec: 14, scale: 1.1, transitionInSeconds: 0.5, transitionOutSeconds: 0.5 };

describe('zoomScaleAtSource (rampas lineares, fonte)', () => {
  it('antes e depois do intervalo = 1', () => {
    expect(zoomScaleAtSource(zoom, 9.9)).toBe(1);
    expect(zoomScaleAtSource(zoom, 14)).toBe(1);
    expect(zoomScaleAtSource(zoom, 20)).toBe(1);
  });

  it('rampa de entrada sobe linearmente de 1 até a escala', () => {
    expect(zoomScaleAtSource(zoom, 10)).toBeCloseTo(1, 6);
    expect(zoomScaleAtSource(zoom, 10.25)).toBeCloseTo(1.05, 6);
    expect(zoomScaleAtSource(zoom, 10.5)).toBeCloseTo(1.1, 6);
  });

  it('platô usa a escala e rampa de saída desce até 1', () => {
    expect(zoomScaleAtSource(zoom, 12)).toBeCloseTo(1.1, 6);
    expect(zoomScaleAtSource(zoom, 13.75)).toBeCloseTo(1.05, 6);
    expect(zoomScaleAtSource(zoom, 13.99)).toBeLessThan(1.01);
  });

  it('rampas zero dão escala cheia no intervalo inteiro', () => {
    const hard = { ...zoom, transitionInSeconds: 0, transitionOutSeconds: 0 };
    expect(zoomScaleAtSource(hard, 10)).toBe(1.1);
    expect(zoomScaleAtSource(hard, 13.9)).toBe(1.1);
  });

  it('entradas inválidas viram 1', () => {
    expect(zoomScaleAtSource(zoom, Number.NaN)).toBe(1);
    expect(zoomScaleAtSource({ ...zoom, scale: 1 }, 12)).toBe(1);
  });
});

describe('zoomTransformAtSource (origem e original vs editado)', () => {
  const windows = [{ ...zoom, focalPointX: 0.5, focalPointY: 0.33 }];

  it('dentro do zoom devolve escala e origem do foco', () => {
    const t = zoomTransformAtSource(windows, 12);
    expect(t.scale).toBeCloseTo(1.1, 6);
    expect(t.origin).toBe('50% 33%');
  });

  it('sem zoom no instante devolve scale 1 e origem central', () => {
    expect(zoomTransformAtSource(windows, 3)).toEqual({ scale: 1, origin: '50% 50%' });
  });

  it('modo Original não recebe zoom: a prévia passa lista vazia', () => {
    expect(zoomTransformAtSource([], 12).scale).toBe(1);
  });
});

describe('validação e escalas', () => {
  it('opções da UI ficam dentro de 1,01–1,12 e padrão 1,08 é válido', () => {
    expect(ZOOM_SCALE_OPTIONS.every((s) => s >= 1.01 && s <= 1.12)).toBe(true);
    expect(Math.max(...ZOOM_SCALE_OPTIONS)).toBeLessThanOrEqual(1.12);
    expect(isSupportedZoomScale(1.08)).toBe(true);
    expect(isSupportedZoomScale(1.18)).toBe(false);
    expect(isSupportedZoomScale(1.25)).toBe(false);
  });

  it('rampas fora de 0,1–1,5 s e soma acima da fala são rejeitadas na UI', () => {
    const base = { startSec: 0, endSec: 1, scale: 1.08 };
    expect(validateZoomInput({ ...base, transitionInSeconds: 0.35, transitionOutSeconds: 0.35 })).toBeNull();
    expect(validateZoomInput({ ...base, transitionInSeconds: 0.05, transitionOutSeconds: 0.35 })).not.toBeNull();
    expect(validateZoomInput({ ...base, transitionInSeconds: 0.6, transitionOutSeconds: 0.6 })).toMatch(/excedem/);
    expect(validateZoomInput({ ...base, scale: 1.18, transitionInSeconds: 0.35, transitionOutSeconds: 0.35 })).not.toBeNull();
  });

  it('busca de conferência cai no fim da rampa de entrada', () => {
    expect(zoomPreviewSeekSeconds(10, 14, 0.35)).toBeCloseTo(10.35, 6);
    expect(zoomPreviewSeekSeconds(10, 10.2, 0.35)).toBeCloseTo(10.1, 6);
  });
});

describe('fonte dos zooms (plano vigente com fallback para sugestões)', () => {
  const plan = {
    appliedEffects: [
      {
        suggestionId: 'z-plan',
        mediaId: 'm1',
        kind: 'zoom',
        origin: 'human',
        scale: 1.1,
        focalPointX: 0.5,
        focalPointY: 0.33,
        sourceInUs: 10_000_000,
        sourceOutUs: 14_000_000,
        transitionInSeconds: 0.35,
        transitionOutSeconds: 0.35
      }
    ]
  } as unknown as EditPlanDto;

  const suggestion = (over: Partial<EditSuggestion>): EditSuggestion => ({
    suggestionId: 'z-sug',
    mediaId: 'm1',
    startSeconds: 2,
    endSeconds: 3,
    kind: 'manual_zoom',
    rationale: '',
    confidenceBand: 'high',
    needsHumanReview: false,
    suggestedAction: 'zoom',
    status: 'Approved',
    author: 'Editor',
    parameters: { zoom: { zoomType: 'punch_in', scale: 1.08, focalPointX: 0.5, focalPointY: 0.5, durationSeconds: 1 } },
    ...over
  });

  it('plano com zoom humano é a fonte', () => {
    const w = resolveZoomWindows(plan, [suggestion({})]);
    expect(w.map((x) => x.suggestionId)).toEqual(['z-plan']);
    expect(w[0].startSec).toBe(10);
    expect(w[0].endSec).toBe(14);
  });

  it('sem zoom no plano usa só sugestões aprovadas, rejeitadas ficam de fora', () => {
    expect(zoomWindowsFromPlan(null)).toEqual([]);
    const fromSug = zoomWindowsFromSuggestions([suggestion({}), suggestion({ suggestionId: 'z-rej', status: 'Rejected' })]);
    expect(fromSug.map((x) => x.suggestionId)).toEqual(['z-sug']);
    expect(resolveZoomWindows(null, [suggestion({})])[0].suggestionId).toBe('z-sug');
  });
});
