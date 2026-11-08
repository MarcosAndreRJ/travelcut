import { describe, it, expect } from 'vitest';
import React from 'react';
import { renderToString } from 'react-dom/server';
import { AudioSyncBadge } from '../AudioSyncBadge';

describe('AudioSyncBadge', () => {
  it('renderiza badge de vinculado', () => {
    const html = renderToString(<AudioSyncBadge isLinked={true} />);
    expect(html).toContain('LINK');
    expect(html).toContain('text-cyan-400');
  });

  it('renderiza badge com desvio quando desvinculado e com offset', () => {
    const html = renderToString(
      <AudioSyncBadge
        isLinked={false}
        syncOffset={{ timelineOffsetFrames: 12, sourceOffsetUs: 400000, inSync: false }}
      />
    );
    expect(html).toContain('+12f');
    expect(html).toContain('text-amber-400');
  });

  it('renderiza badge neutro quando desvinculado mas alinhado', () => {
    const html = renderToString(
      <AudioSyncBadge
        isLinked={false}
        syncOffset={{ timelineOffsetFrames: 0, sourceOffsetUs: 0, inSync: true }}
      />
    );
    expect(html).toContain('±0f');
    expect(html).toContain('text-slate-300');
  });
});
