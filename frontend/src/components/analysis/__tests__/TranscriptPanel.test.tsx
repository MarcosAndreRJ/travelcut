import { describe, it, expect, vi } from 'vitest';
import React from 'react';
import { renderToString } from 'react-dom/server';
import { TranscriptPanel, restorableCutsIn } from '../TranscriptPanel';
import { CutFeedbackBanner, CutRestoreButton } from '../CutEditControls';
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

  it('suporta callbacks editoriais de corte e zoom', () => {
    const cutEditing = {
      removedIntervals: [],
      canUndo: false,
      canRedo: false,
      onCut: vi.fn(),
      onUndo: vi.fn(),
      onRedo: vi.fn()
    };
    const zoomEditing = {
      zoomWindows: [],
      onApply: vi.fn(),
      onRemove: vi.fn()
    };

    const html = renderToString(
      <TranscriptPanel
        transcript={mockTranscript}
        hesitations={[]}
        selectedSec={2.0}
        onSeek={vi.fn()}
        cutEditing={cutEditing}
        zoomEditing={zoomEditing}
      />
    );

    expect(html).toBeTruthy();
    expect(html).not.toContain('sugestão pendente');
  });

  it('marca como cortadas as palavras de intervalos confirmados pelo host', () => {
    const html = renderToString(
      <TranscriptPanel
        transcript={mockTranscript}
        hesitations={[]}
        selectedSec={null}
        onSeek={vi.fn()}
        cutEditing={{
          removedIntervals: [{ suggestionId: 's-1', startSec: 1.6, endSec: 2.2 }],
          canUndo: true,
          canRedo: false,
          onCut: vi.fn(),
          onUndo: vi.fn(),
          onRedo: vi.fn()
        }}
      />
    );

    expect(html).toContain('line-through');
    expect(html).toContain('(Cortado)');
  });

  it('sem cutEditing não há marcações de corte', () => {
    const html = renderToString(
      <TranscriptPanel transcript={mockTranscript} hesitations={[]} selectedSec={null} onSeek={vi.fn()} />
    );
    expect(html).not.toContain('line-through');
  });

  it('confirmação de corte traz o botão Desfazer na própria mensagem', () => {
    const html = renderToString(
      <TranscriptPanel
        transcript={mockTranscript}
        hesitations={[]}
        selectedSec={null}
        onSeek={vi.fn()}
        cutEditing={{
          removedIntervals: [{ suggestionId: 's-1', startSec: 1.6, endSec: 2.2 }],
          canUndo: true,
          canRedo: false,
          feedback: {
            tone: 'success',
            text: 'Corte aplicado.',
            action: { label: 'Desfazer', op: 'undo', suggestionIds: ['s-1'] }
          },
          onCut: vi.fn(),
          onUndo: vi.fn(),
          onRedo: vi.fn()
        }}
      />
    );
    expect(html).toContain('Corte aplicado.');
    expect(html).toContain('Desfazer');
  });

  it('restorableCutsIn devolve só os cortes sobrepostos, com o suggestionId do host', () => {
    const removed = [
      { suggestionId: 's-1', startSec: 1.6, endSec: 2.2 },
      { suggestionId: 's-2', startSec: 5.0, endSec: 6.0 }
    ];
    expect(restorableCutsIn(2.0, 2.5, removed)).toEqual([{ suggestionId: 's-1', startSec: 1.6, endSec: 2.2 }]);
    expect(restorableCutsIn(2.2, 2.5, removed)).toEqual([]);
    expect(restorableCutsIn(3.0, 4.0, removed)).toEqual([]);
  });
});

describe('CutRestoreButton e CutFeedbackBanner', () => {
  it('Restaurar trecho aparece quando a seleção atinge palavras cortadas', () => {
    expect(renderToString(<CutRestoreButton count={1} onRestore={vi.fn()} />)).toContain('Restaurar trecho');
  });

  it('Restaurar trecho não aparece sem corte sobreposto', () => {
    expect(renderToString(<CutRestoreButton count={0} onRestore={vi.fn()} />)).toBe('');
  });

  it('a confirmação mostra o erro real do host com role alert', () => {
    const html = renderToString(
      <CutFeedbackBanner feedback={{ tone: 'error', text: 'Não foi possível restaurar o trecho: gravação falhou' }} />
    );
    expect(html).toContain('gravação falhou');
    expect(html).toContain('role="alert"');
    expect(html).not.toContain('Desfazer');
  });
});
