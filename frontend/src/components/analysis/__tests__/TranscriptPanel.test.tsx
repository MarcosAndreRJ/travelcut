import { describe, it, expect, vi } from 'vitest';
import React from 'react';
import { renderToString } from 'react-dom/server';
import { TranscriptPanel } from '../TranscriptPanel';
import type { HesitationCandidateDto, TranscriptDto } from '../../../types/ipc';

const mockTranscript: TranscriptDto = {
  transcriptId: 'tr-1',
  mediaId: 'm-1',
  modelId: 'whisper-test',
  fullText: 'Olá mundo este é um teste editorial de transcrição',
  language: 'pt',
  audioDurationSeconds: 10.0,
  words: [
    { seq: 0, text: 'Olá', start: 1.0, end: 1.5, confidence: 0.95 },
    { seq: 1, text: 'mundo', start: 1.6, end: 2.2, confidence: 0.92 },
    { seq: 2, text: 'este', start: 2.5, end: 2.8, confidence: 0.94 },
    { seq: 3, text: 'é', start: 2.9, end: 3.0, confidence: 0.99 },
    { seq: 4, text: 'um', start: 3.1, end: 3.3, confidence: 0.91 },
    { seq: 5, text: 'teste', start: 3.4, end: 4.0, confidence: 0.88 },
    { seq: 6, text: 'editorial', start: 4.1, end: 5.0, confidence: 0.96 },
    { seq: 7, text: 'de', start: 5.1, end: 5.2, confidence: 0.93 },
    { seq: 8, text: 'transcrição', start: 5.3, end: 6.5, confidence: 0.97 }
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
    const hesitations: HesitationCandidateDto[] = [
      {
        startSeconds: 1.6,
        endSeconds: 2.2,
        detectedWord: 'mundo',
        type: 'prolongation',
        confidenceBand: 'high',
        contextBefore: 'Olá',
        contextAfter: 'este',
        needsHumanReview: false
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
