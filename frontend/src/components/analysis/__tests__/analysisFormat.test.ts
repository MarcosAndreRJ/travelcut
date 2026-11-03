import { describe, expect, it } from 'vitest';
import type { EditSuggestion } from '../../../types/ipc';
import {
  formatClock,
  formatDuration,
  formatSeconds,
  hesitationTypeLabel,
  intervalsOverlap,
  isApprovedStatus,
  isBatchApprovable,
  needsHumanReviewFlag,
  suggestedActionLabel,
  suggestionKindLabel
} from '../analysisFormat';

const sug = (over: Partial<EditSuggestion> & Pick<EditSuggestion, 'suggestionId' | 'kind'>): EditSuggestion => ({
  mediaId: 'med-1',
  startSeconds: 1,
  endSeconds: 2,
  rationale: 'DEMO',
  confidenceBand: 'high',
  needsHumanReview: false,
  suggestedAction: 'cut',
  status: 'Proposed',
  author: 'DEMO',
  ...over
});

describe('analysisFormat: lote "aprovar silêncios confiáveis" (M2)', () => {
  it('aceita somente silêncio com confiança alta, pendente e sem revisão humana', () => {
    expect(isBatchApprovable(sug({ suggestionId: 'ok', kind: 'silence' }))).toBe(true);
  });

  it('pausa NÃO entra no lote (pausas de ritmo/humor exigem revisão item a item)', () => {
    expect(isBatchApprovable(sug({ suggestionId: 'pause', kind: 'pause' }))).toBe(false);
  });

  it('a comparação de categoria ignora maiúsculas', () => {
    expect(isBatchApprovable(sug({ suggestionId: 'upper', kind: 'SILENCE' }))).toBe(true);
  });

  it('confiança média ou baixa fica de fora (a faixa alta é heurística não calibrada)', () => {
    expect(isBatchApprovable(sug({ suggestionId: 'med', kind: 'silence', confidenceBand: 'medium' }))).toBe(false);
    expect(isBatchApprovable(sug({ suggestionId: 'low', kind: 'silence', confidenceBand: 'low' }))).toBe(false);
  });

  it('já decidido ou com revisão humana fica de fora', () => {
    expect(isBatchApprovable(sug({ suggestionId: 'appr', kind: 'silence', status: 'Approved' }))).toBe(false);
    expect(isBatchApprovable(sug({ suggestionId: 'rej', kind: 'silence', status: 'Rejected' }))).toBe(false);
    expect(isBatchApprovable(sug({ suggestionId: 'hum', kind: 'silence', needsHumanReview: true }))).toBe(false);
  });

  it('demais categorias nunca entram no lote', () => {
    for (const kind of ['filler', 'repetition', 'false_start', 'prolongation', 'hesitation', 'speedbroll', 'subtlezoom']) {
      expect(isBatchApprovable(sug({ suggestionId: kind, kind }))).toBe(false);
    }
  });
});

describe('analysisFormat: rótulos e estados', () => {
  it('rótulos de categoria e ação sugerida', () => {
    expect(suggestionKindLabel('silence')).toBe('Silêncio prolongado');
    expect(suggestionKindLabel('zz_desconhecido')).toBe('zz_desconhecido');
    expect(suggestedActionLabel('speed2x')).toBe('Acelerar 2x');
    expect(hesitationTypeLabel('filler')).toBe('Muleta verbal');
  });

  it('"Accepted" é aceito por compatibilidade; "Approved" é o domínio', () => {
    expect(isApprovedStatus('Approved')).toBe(true);
    expect(isApprovedStatus('Accepted')).toBe(true);
    expect(isApprovedStatus('Proposed')).toBe(false);
  });

  it('flag de revisão humana aceita needsHumanReview ou ação "review"', () => {
    expect(needsHumanReviewFlag(sug({ suggestionId: 'a', kind: 'silence', needsHumanReview: true }))).toBe(true);
    expect(needsHumanReviewFlag(sug({ suggestionId: 'b', kind: 'silence', suggestedAction: 'review' }))).toBe(true);
    expect(needsHumanReviewFlag(sug({ suggestionId: 'c', kind: 'silence' }))).toBe(false);
  });
});

describe('analysisFormat: formatação de tempo', () => {
  it('mm:ss.d para tempo de mídia; inválidos viram traço', () => {
    expect(formatClock(12.34)).toBe('00:12.3');
    expect(formatClock(75)).toBe('01:15.0');
    expect(formatClock(null)).toBe('—');
    expect(formatClock(-1)).toBe('—');
  });

  it('mm:ss inteiro para duração', () => {
    expect(formatDuration(125)).toBe('02:05');
    expect(formatDuration(undefined)).toBe('—');
  });

  it('segundos com uma casa; não finito vira traço', () => {
    expect(formatSeconds(12.34)).toBe('12.3 s');
    expect(formatSeconds(Number.NaN)).toBe('—');
  });

  it('sobreposição de intervalos exclui apenas encostados', () => {
    expect(intervalsOverlap(0, 2, 1, 3)).toBe(true);
    expect(intervalsOverlap(0, 2, 2, 3)).toBe(false);
  });
});
