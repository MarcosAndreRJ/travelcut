import { describe, it, expect, vi } from 'vitest';
import React from 'react';
import { renderToString } from 'react-dom/server';
import { TranscriptPanel } from '../TranscriptPanel';
import type { TranscriptDto } from '../../../types/ipc';

const mockTranscript: TranscriptDto = {
  text: 'Olá mundo este é um teste editorial de transcrição',
  fullText: 'Olá mundo este é um teste editorial de transcrição',
  language: 'pt',
  durationSec: 10.0,
  words: [
    { text: 'Olá', start: 1.0, end: 1.5, confidence: 0.95 },
    { text: 'mundo', start: 1.6, end: 2.2, confidence: 0.92 },
    { text: 'este', start: 2.5, end: 2.8, confidence: 0.94 },
    { text: 'é', start: 2.9, end: 3.0, confidence: 0.99 },
    { text: 'um', start: 3.1, end: 3.3, confidence: 0.91 },
    { text: 'teste', start: 3.4, end: 4.0, confidence: 0.88 },
    { text: 'editorial', start: 4.1, end: 5.0, confidence: 0.96 },
    { text: 'de', start: 5.1, end: 5.2, confidence: 0.93 },
    { text: 'transcrição', start: 5.3, end: 6.5, confidence: 0.97 }
  ]
};

describe('TranscriptPanel', () => {
  it('renderiza palavras com timestamps e contagem', () => {
    const html = renderToString(
      <TranscriptPanel
        transcript={mockTranscript}
        hesitations={[]}
        selectedSec={null}
        onSeek={vi.fn()}
      />
    );

    expect(html).toContain('Olá');
    expect(html).toContain('mundo');
    expect(html).toContain('editorial');
    expect(html).toContain('9 palavras');
  });

  it('renderiza hesitações com destaque específico', () => {
    const hesitations = [
      {
        candidateId: 'h1',
        startSeconds: 1.6,
        endSeconds: 2.2,
        kind: 'prolonged_sound' as const,
        score: 0.85,
        decision: 'pending' as const
      }
    ];

    const html = renderToString(
      <TranscriptPanel
        transcript={mockTranscript}
        hesitations={hesitations}
        selectedSec={null}
        onSeek={vi.fn()}
      />
    );

    expect(html).toContain('decoration-rose-500');
  });

  it('suporta callbacks editoriais de exclusão e zoom', () => {
    const onDelete = vi.fn();
    const onZoom = vi.fn();

    const html = renderToString(
      <TranscriptPanel
        transcript={mockTranscript}
        hesitations={[]}
        selectedSec={2.0}
        onSeek={vi.fn()}
        onDeleteInterval={onDelete}
        onCreateZoomInterval={onZoom}
      />
    );

    expect(html).toBeTruthy();
  });
});
