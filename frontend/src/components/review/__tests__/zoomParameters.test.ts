import { describe, it, expect } from 'vitest';
import {
  DEFAULT_ZOOM_CONFIG,
  buildZoomApproveRequest,
  zoomConfigFromParameters,
  zoomConfigToParameters
} from '../EditorialZoomControls';
import type { SmartZoomParametersDto } from '../../../types/ipc';

const zoomFromHost: SmartZoomParametersDto = {
  zoomType: 'punch_in',
  scale: 1.08,
  focalPointX: 0.5,
  focalPointY: 0.35,
  durationSeconds: 4,
  focalPointKnown: true
};

describe('mapeamento config de zoom -> parameters.zoom', () => {
  it('sem zoom (1.0x) envia parameters.zoom.none = true com escala 1.0', () => {
    expect(zoomConfigToParameters(DEFAULT_ZOOM_CONFIG, 4)).toEqual({
      parameters: {
        zoomType: 'punch_in',
        scale: 1.0,
        focalPointX: 0.5,
        focalPointY: 0.5,
        durationSeconds: 0,
        focalPointKnown: true,
        none: true
      },
      none: true,
      scaleClamped: false
    });
  });

  it('presets do intervalo do host (1,01–1,12) não são limitados', () => {
    for (const scale of [1.04, 1.08, 1.12]) {
      const { parameters, scaleClamped, none } = zoomConfigToParameters({ ...DEFAULT_ZOOM_CONFIG, scale }, 2);
      expect(parameters.scale).toBe(scale);
      expect(scaleClamped).toBe(false);
      expect(none).toBe(false);
      expect(parameters.none).toBeUndefined();
    }
  });

  it('converte escala, offsets em foco 0..1 e duração do intervalo', () => {
    const { parameters, scaleClamped } = zoomConfigToParameters(
      { ...DEFAULT_ZOOM_CONFIG, scale: 1.1, anchor: 'custom', offsetX: 10, offsetY: -20 },
      3.5
    );
    expect(scaleClamped).toBe(false);
    expect(parameters).toEqual({
      zoomType: 'punch_in',
      scale: 1.1,
      focalPointX: 0.6,
      focalPointY: 0.3,
      durationSeconds: 3.5,
      focalPointKnown: true
    });
  });

  it('limita escalas acima de 1.12 à faixa do host e sinaliza', () => {
    const { parameters, scaleClamped } = zoomConfigToParameters({ ...DEFAULT_ZOOM_CONFIG, scale: 1.25 }, 2);
    expect(parameters.scale).toBe(1.12);
    expect(scaleClamped).toBe(true);
  });

  it('mantém foco dentro de 0..1 mesmo com offsets extremos', () => {
    const { parameters } = zoomConfigToParameters({ ...DEFAULT_ZOOM_CONFIG, scale: 1.05, offsetX: 50, offsetY: -50 }, 1);
    expect(parameters.focalPointX).toBe(1);
    expect(parameters.focalPointY).toBe(0);
  });
});

describe('inicialização do controle a partir de suggestion.parameters.zoom', () => {
  it('ausente = sem zoom (DEFAULT)', () => {
    expect(zoomConfigFromParameters(undefined)).toEqual(DEFAULT_ZOOM_CONFIG);
    expect(zoomConfigFromParameters(null)).toEqual(DEFAULT_ZOOM_CONFIG);
  });

  it('none = true ou escala 1.0 do host = sem zoom explícito', () => {
    expect(zoomConfigFromParameters({ ...zoomFromHost, scale: 1.0, none: true })).toEqual(DEFAULT_ZOOM_CONFIG);
    expect(zoomConfigFromParameters({ ...zoomFromHost, scale: 1.0 })).toEqual(DEFAULT_ZOOM_CONFIG);
  });

  it('reflete escala e âncora terço superior do host', () => {
    expect(zoomConfigFromParameters(zoomFromHost)).toEqual(
      expect.objectContaining({ scale: 1.08, anchor: 'top-third', offsetX: 0, offsetY: -15 })
    );
  });

  it('foco centralizado vira anchor center e foco fora dos presets vira custom', () => {
    expect(zoomConfigFromParameters({ ...zoomFromHost, focalPointY: 0.5 }).anchor).toBe('center');
    expect(zoomConfigFromParameters({ ...zoomFromHost, focalPointX: 0.7, focalPointY: 0.5 })).toEqual(
      expect.objectContaining({ anchor: 'custom', offsetX: 20, offsetY: 0 })
    );
  });

  it('round-trip preserva os parâmetros do host dentro da faixa', () => {
    const config = zoomConfigFromParameters(zoomFromHost);
    const { parameters } = zoomConfigToParameters(config, zoomFromHost.durationSeconds);
    expect(parameters).toEqual(zoomFromHost);
  });
});

describe('chamada de decisão com parameters.zoom', () => {
  it('monta approve com parameters.zoom e reviewer', () => {
    expect(buildZoomApproveRequest('sug-1', zoomFromHost, 'revisor')).toEqual({
      suggestionId: 'sug-1',
      action: 'approve',
      reviewer: 'revisor',
      parameters: { zoom: zoomFromHost }
    });
  });
});
