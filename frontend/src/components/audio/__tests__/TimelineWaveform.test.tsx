import { describe, it, expect, beforeEach } from 'vitest';
import React from 'react';
import { renderToString } from 'react-dom/server';
import {
  TimelineWaveform,
  buildWaveformCacheKey,
  clearWaveformCache,
  getCachedWaveform,
  computeWaveformBuckets,
  setCachedWaveform
} from '../TimelineWaveform';

describe('TimelineWaveform', () => {
  beforeEach(() => {
    clearWaveformCache();
  });

  it('geração de chave de cache consistente e determinística', () => {
    const key1 = buildWaveformCacheKey('media-1', 0, 5, 100);
    const key2 = buildWaveformCacheKey('media-1', 0.0, 5.0, 100);
    expect(key1).toBe('media-1:0.000:5.000:100');
    expect(key1).toBe(key2);
  });

  it('armazena e recupera picos no cache em memória', () => {
    const mockPeaks = [0.1, 0.4, 0.8, 0.3];
    const key = buildWaveformCacheKey('media-abc', 2.5, 7.5, computeWaveformBuckets(200));
    expect(getCachedWaveform(key)).toBeNull();

    setCachedWaveform(key, mockPeaks);
    expect(getCachedWaveform(key)).toEqual(mockPeaks);
  });

  it('renderiza picos SVG quando disponíveis no cache', () => {
    const key = buildWaveformCacheKey('m1', 0, 10, computeWaveformBuckets(200));
    setCachedWaveform(key, [0.2, 0.8, 0.5, 0.9]);

    const html = renderToString(
      <TimelineWaveform
        projectId="p1"
        mediaId="m1"
        sourceInSeconds={0}
        sourceOutSeconds={10}
        widthPx={200}
        tone="cyan"
      />
    );

    expect(html).toContain('<svg');
    expect(html).toContain('fill="#22d3ee"');
  });

  it('aplica opacidade reduzida quando a faixa ou clipe está mutado', () => {
    const key = buildWaveformCacheKey('m1', 0, 10, computeWaveformBuckets(200));
    setCachedWaveform(key, [0.5, 0.5]);

    const htmlMuted = renderToString(
      <TimelineWaveform
        projectId="p1"
        mediaId="m1"
        sourceInSeconds={0}
        sourceOutSeconds={10}
        widthPx={200}
        isMuted={true}
      />
    );

    expect(htmlMuted).toContain('opacity-30');
  });

  it('não inventa picos artificiais e exibe estado de carregamento inicial', () => {
    const html = renderToString(
      <TimelineWaveform
        projectId="p1"
        mediaId="m-nova"
        sourceInSeconds={0}
        sourceOutSeconds={10}
        widthPx={200}
      />
    );

    // Sem picos em cache, nenhum rect de onda é renderizado
    expect(html).not.toContain('<svg');
  });
});
