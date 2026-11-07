import { describe, it, expect } from 'vitest';
import { DEFAULT_ZOOM_CONFIG, PRESET_SCALES } from '../EditorialZoomControls';

describe('EditorialZoomControls', () => {
  it('tem configuração padrão em 1.0x (sem zoom)', () => {
    expect(DEFAULT_ZOOM_CONFIG.scale).toBe(1.0);
    expect(DEFAULT_ZOOM_CONFIG.anchor).toBe('center');
    expect(DEFAULT_ZOOM_CONFIG.offsetX).toBe(0);
    expect(DEFAULT_ZOOM_CONFIG.offsetY).toBe(0);
    expect(DEFAULT_ZOOM_CONFIG.mode).toBe('hold');
    expect(DEFAULT_ZOOM_CONFIG.showSafeArea).toBe(false);
  });

  it('fornece presets editoriais válidos', () => {
    expect(PRESET_SCALES).toContainEqual(expect.objectContaining({ value: 1.0 }));
    expect(PRESET_SCALES).toContainEqual(expect.objectContaining({ value: 1.1 }));
    expect(PRESET_SCALES).toContainEqual(expect.objectContaining({ value: 1.15 }));
    expect(PRESET_SCALES).toContainEqual(expect.objectContaining({ value: 1.25 }));
  });
});
