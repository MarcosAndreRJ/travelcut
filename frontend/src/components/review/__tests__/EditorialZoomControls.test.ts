import { describe, it, expect } from 'vitest';
import { DEFAULT_ZOOM_CONFIG, HOST_ZOOM_SCALE_MAX, HOST_ZOOM_SCALE_MIN, PRESET_SCALES } from '../EditorialZoomControls';

describe('EditorialZoomControls', () => {
  it('tem configuração padrão em 1.0x (sem zoom)', () => {
    expect(DEFAULT_ZOOM_CONFIG.scale).toBe(1.0);
    expect(DEFAULT_ZOOM_CONFIG.anchor).toBe('center');
    expect(DEFAULT_ZOOM_CONFIG.offsetX).toBe(0);
    expect(DEFAULT_ZOOM_CONFIG.offsetY).toBe(0);
    expect(DEFAULT_ZOOM_CONFIG.mode).toBe('hold');
    expect(DEFAULT_ZOOM_CONFIG.showSafeArea).toBe(false);
  });

  it('presets ficam dentro da faixa do host e incluem "Sem zoom"', () => {
    const values = PRESET_SCALES.map((p) => p.value);
    expect(PRESET_SCALES).toContainEqual(expect.objectContaining({ value: 1.0, label: 'Sem zoom' }));
    for (const v of values.filter((v) => v > 1.0)) {
      expect(v).toBeGreaterThanOrEqual(HOST_ZOOM_SCALE_MIN);
      expect(v).toBeLessThanOrEqual(HOST_ZOOM_SCALE_MAX);
      expect(v).toBeGreaterThanOrEqual(1.01);
      expect(v).toBeLessThanOrEqual(1.12);
    }
    expect(values).not.toContain(1.15);
    expect(values).not.toContain(1.2);
    expect(values).not.toContain(1.25);
  });
});
