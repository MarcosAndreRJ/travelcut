import { describe, expect, it } from 'vitest';
import { planZoomActiveAt, zoomCurveLabel, zoomOriginCss } from '../zoomPresentation';

describe('zoomOriginCss (foco do plano -> transform-origin)', () => {
  it('converte foco 0..1 em porcentagem e usa centro quando ausente', () => {
    expect(zoomOriginCss(0.3, 0.2)).toBe('30% 20%');
    expect(zoomOriginCss(null, undefined)).toBe('50% 50%');
    expect(zoomOriginCss(Number.NaN, 2)).toBe('50% 100%');
  });
});

describe('planZoomActiveAt (duração do zoom)', () => {
  const base = { zoom: 1.08, zoomFocalPointX: 0.3, zoomFocalPointY: 0.2 };

  it('sem zoom ou escala <= 1 não há zoom', () => {
    expect(planZoomActiveAt({ zoom: null }, 0)).toBeNull();
    expect(planZoomActiveAt({ zoom: 1.0 }, 0)).toBeNull();
  });

  it('duração ausente aplica ao clipe inteiro', () => {
    expect(planZoomActiveAt(base, 0)).toEqual({ scale: 1.08, originX: 30, originY: 20 });
    expect(planZoomActiveAt({ ...base, zoomDurationSeconds: null }, 999)).not.toBeNull();
  });

  it('duração definida corta o zoom no instante do fim', () => {
    const fields = { ...base, zoomDurationSeconds: 2 };
    expect(planZoomActiveAt(fields, 1.9)).not.toBeNull();
    expect(planZoomActiveAt(fields, 2)).toBeNull();
  });

  it('foco ausente cai no centro e tempo negativo ou NaN não gera zoom', () => {
    expect(planZoomActiveAt({ zoom: 1.1 }, 0)).toEqual({ scale: 1.1, originX: 50, originY: 50 });
    expect(planZoomActiveAt(base, -0.1)).toBeNull();
    expect(planZoomActiveAt(base, Number.NaN)).toBeNull();
  });
});

describe('zoomCurveLabel (curva só rotulada como aproximada)', () => {
  it('rotula punch_in e push_in como aproximados', () => {
    expect(zoomCurveLabel('punch_in')).toContain('aproximado');
    expect(zoomCurveLabel('push_in')).toContain('push-in');
    expect(zoomCurveLabel(null)).toContain('punch-in');
  });
});
