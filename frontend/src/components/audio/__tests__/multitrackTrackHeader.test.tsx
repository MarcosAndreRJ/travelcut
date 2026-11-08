import { describe, it, expect, vi } from 'vitest';
import React from 'react';
import { renderToString } from 'react-dom/server';
import { TrackHeaderControls } from '../TrackHeaderControls';
import { DEFAULT_TRACK_STATES } from '../multitrackModel';

describe('TrackHeaderControls', () => {
  it('renderiza controles com nome, mute e slider de volume', () => {
    const a1State = DEFAULT_TRACK_STATES.A1;
    const html = renderToString(
      <TrackHeaderControls
        trackId="A1"
        label="A1 — áudio (diálogo)"
        kind="dialogue"
        audioState={a1State}
        onVolumeChange={vi.fn()}
        onMuteToggle={vi.fn()}
        onLockToggle={vi.fn()}
      />
    );
    expect(html).toContain('A1');
    expect(html).toContain('A1 — áudio (diálogo)');
    expect(html).toContain('aria-label="Mutar A1"');
    expect(html).toContain('aria-label="Trancar A1"');
    expect(html).toContain('aria-label="Volume da faixa A1"');
  });

  it('exibe indicador de mudo ativo', () => {
    const mutedState = { ...DEFAULT_TRACK_STATES.A1, isMuted: true };
    const html = renderToString(
      <TrackHeaderControls
        trackId="A1"
        label="A1 — áudio (diálogo)"
        kind="dialogue"
        audioState={mutedState}
        onVolumeChange={vi.fn()}
        onMuteToggle={vi.fn()}
        onLockToggle={vi.fn()}
      />
    );
    expect(html).toContain('bg-rose-950/80');
  });
});
