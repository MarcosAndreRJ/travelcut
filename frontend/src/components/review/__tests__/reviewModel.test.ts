import { describe, expect, it } from 'vitest';
import type { EditSuggestion, TranscriptWordDto } from '../../../types/ipc';
import {
  DEFAULT_REVIEW_FILTERS,
  countByStatus,
  describeDecisionError,
  effectiveInterval,
  filterSuggestions,
  formatDraft,
  isAdjusted,
  kindOptionsOf,
  parseDraft,
  decisionPlanErrorMessage,
  planRemovedDetail,
  playerWindowOf,
  reviewKindLabel,
  reviewWindowOf,
  REVIEW_WINDOW_SECONDS,
  stepDraft,
  statusLabelOf,
  validateModifyDraft,
  wordInInterval,
  wordsInWindow,
  type ReviewFilters
} from '../reviewModel';

const sug = (over: Partial<EditSuggestion> & Pick<EditSuggestion, 'suggestionId'>): EditSuggestion => ({
  mediaId: 'med-1',
  startSeconds: 10,
  endSeconds: 12,
  kind: 'silence',
  rationale: 'DEMO',
  confidenceBand: 'high',
  needsHumanReview: false,
  suggestedAction: 'cut',
  status: 'Proposed',
  author: 'DEMO',
  ...over
});

const LIST: EditSuggestion[] = [
  sug({ suggestionId: 'p1', status: 'Proposed', kind: 'silence', confidenceBand: 'high' }),
  sug({ suggestionId: 'p2', status: 'Proposed', kind: 'filler', confidenceBand: 'low', needsHumanReview: true }),
  sug({ suggestionId: 'a1', status: 'Approved', kind: 'Silence', confidenceBand: 'medium' }),
  sug({ suggestionId: 'r1', status: 'Rejected', kind: 'repetition', confidenceBand: 'high', suggestedAction: 'review' }),
  sug({ suggestionId: 'd1', status: 'Deferred', kind: 'pause', confidenceBand: 'medium' }),
  sug({ suggestionId: 'x1', status: 'Proposed', kind: 'zoom_extra', confidenceBand: 'high' })
];

describe('reviewModel: filtros da fila', () => {
  const run = (over: Partial<ReviewFilters>) => filterSuggestions(LIST, { ...DEFAULT_REVIEW_FILTERS, ...over }).map((s) => s.suggestionId);

  it('filtro padrão mostra somente pendentes', () => {
    expect(DEFAULT_REVIEW_FILTERS.status).toBe('pending');
    expect(run({})).toEqual(['p1', 'p2', 'x1']);
  });

  it('filtra por status aprovado, rejeitado, adiado e todos', () => {
    expect(run({ status: 'approved' })).toEqual(['a1']);
    expect(run({ status: 'rejected' })).toEqual(['r1']);
    expect(run({ status: 'deferred' })).toEqual(['d1']);
    expect(run({ status: 'all' })).toHaveLength(LIST.length);
  });

  it('categoria compara em minúsculas', () => {
    expect(run({ status: 'all', kind: 'silence' })).toEqual(['p1', 'a1']);
  });

  it('confiança filtra pela faixa recebida', () => {
    expect(run({ status: 'all', confidence: 'high' })).toEqual(['p1', 'r1', 'x1']);
  });

  it('somente revisão humana aceita flag do host ou ação "review"', () => {
    expect(run({ status: 'all', humanOnly: true })).toEqual(['p2', 'r1']);
  });
});

describe('reviewModel: contadores e rótulos', () => {
  it('conta por status a partir da lista recebida', () => {
    expect(countByStatus(LIST)).toEqual({ all: 6, pending: 3, approved: 1, rejected: 1, deferred: 1, applied: 1, conflict: 0 });
  });

  it('lista vazia zera os contadores', () => {
    expect(countByStatus([])).toEqual({ all: 0, pending: 0, approved: 0, rejected: 0, deferred: 0, applied: 0, conflict: 0 });
  });

  it('categorias conhecidas vêm primeiro na ordem definida, desconhecidas em ordem alfabética', () => {
    const kinds = kindOptionsOf([
      sug({ suggestionId: 'a', kind: 'zeta' }),
      sug({ suggestionId: 'b', kind: 'repetition' }),
      sug({ suggestionId: 'c', kind: 'alfa' }),
      sug({ suggestionId: 'd', kind: 'silence' }),
      sug({ suggestionId: 'e', kind: 'SILENCE' })
    ]);
    expect(kinds).toEqual(['silence', 'repetition', 'alfa', 'zeta']);
  });

  it('rótulo PT-BR de categoria; desconhecida mostra o valor recebido', () => {
    expect(reviewKindLabel('FILLER')).toBe('Vício de fala');
    expect(reviewKindLabel('misterio')).toBe('misterio');
  });

  it('status "Aprovado com ajuste" aparece somente quando o intervalo mudou', () => {
    const adjusted = sug({ suggestionId: 'm', status: 'Approved', effectiveStartSeconds: 10.5, effectiveEndSeconds: 12 });
    expect(isAdjusted(adjusted)).toBe(true);
    expect(statusLabelOf(adjusted)).toBe('Aprovado com ajuste');
    expect(statusLabelOf(sug({ suggestionId: 'n', status: 'Approved' }))).toBe('Aprovado');
    expect(statusLabelOf(sug({ suggestionId: 'q', status: 'Proposed' }))).toBe('Pendente');
  });

  it('intervalo efetivo usa o ajuste quando existe', () => {
    expect(effectiveInterval(sug({ suggestionId: 'e' }))).toEqual({ start: 10, end: 12 });
    expect(effectiveInterval(sug({ suggestionId: 'e', effectiveStartSeconds: 11, effectiveEndSeconds: 11.5 }))).toEqual({ start: 11, end: 11.5 });
  });
});

describe('reviewModel: tempo removido vem do plano (M1)', () => {
  it('sem plano, pede para gerar o plano', () => {
    expect(planRemovedDetail({ status: 'none' })).toBe('Gere o plano para ver o tempo removido');
  });

  it('erro ou carregamento não exibem número', () => {
    expect(planRemovedDetail({ status: 'error' })).toBe('Não foi possível ler o plano para medir o tempo removido');
    expect(planRemovedDetail({ status: 'loading' })).toContain('Lendo o plano');
  });

  it('com plano, mostra a revisão e o tempo do summary (microssegundos)', () => {
    expect(planRemovedDetail({ status: 'ready', revision: 3, removedSourceUs: 12_345_000 })).toBe('Removido no plano r3: 12,3 s');
    expect(planRemovedDetail({ status: 'ready', revision: 1, removedSourceUs: 0 })).toBe('Removido no plano r1: 0,0 s');
  });

  it('valor não finito vira traço, nunca zero inventado', () => {
    expect(planRemovedDetail({ status: 'ready', revision: 2, removedSourceUs: Number.NaN })).toBe('Removido no plano r2: —');
  });
});

describe('reviewModel: validação do editor de modify', () => {
  const win = { start: 8, end: 14 };

  it('aceita intervalo válido dentro da janela, com vírgula decimal', () => {
    expect(validateModifyDraft('9,5', '12', win)).toEqual({ ok: true, start: 9.5, end: 12 });
  });

  it('campos vazios ou não numéricos são rejeitados', () => {
    expect(validateModifyDraft('', '12', win)).toMatchObject({ ok: false });
    expect(validateModifyDraft('abc', '12', win)).toMatchObject({ ok: false });
  });

  it('valores negativos são rejeitados', () => {
    expect(validateModifyDraft('-1', '12', win)).toEqual({ ok: false, message: 'Entrada e saída não podem ser negativas.' });
  });

  it('saída precisa ser maior que a entrada', () => {
    expect(validateModifyDraft('12', '12', win)).toEqual({ ok: false, message: 'A saída deve ser maior que a entrada.' });
    expect(validateModifyDraft('12', '11', win)).toMatchObject({ ok: false });
  });

  it('o intervalo precisa ficar dentro da janela de revisão', () => {
    expect(validateModifyDraft('7,9', '12', win)).toEqual({ ok: false, message: 'O intervalo deve ficar dentro da janela de revisão.' });
    expect(validateModifyDraft('9', '14,5', win)).toMatchObject({ ok: false });
  });

  it('arredonda para milissegundos ao ler e ao escrever', () => {
    expect(parseDraft('1,23456')).toBe(1.235);
    expect(formatDraft(2.5)).toBe('2.5');
  });

  it('ajuste de passo nunca fica negativo', () => {
    expect(stepDraft('0,05', -0.1)).toBe('0');
    expect(stepDraft('1,2', 0.1)).toBe('1.3');
    expect(stepDraft('', 0.1)).toBe('0.1');
  });
});

describe('reviewModel: janelas de revisão e transcrição', () => {
  it('janela de revisão é ancorada no intervalo ORIGINAL e não vai abaixo de zero', () => {
    expect(reviewWindowOf(sug({ suggestionId: 'w', startSeconds: 1, endSeconds: 2 }))).toEqual({
      start: 0,
      end: 2 + REVIEW_WINDOW_SECONDS
    });
    const adjusted = sug({ suggestionId: 'w2', startSeconds: 20, endSeconds: 22, effectiveStartSeconds: 21, effectiveEndSeconds: 21.5 });
    expect(reviewWindowOf(adjusted)).toEqual({ start: 17, end: 25 });
  });

  it('janela do player acrescenta contexto e limita o início em zero', () => {
    expect(playerWindowOf({ start: 1, end: 4 })).toEqual({ start: 0, end: 6 });
    expect(playerWindowOf({ start: 10, end: 11 })).toEqual({ start: 8, end: 13 });
  });

  const words: TranscriptWordDto[] = [
    { seq: 1, start: 0, end: 1, text: 'antes', confidence: 0.9 },
    { seq: 2, start: 1, end: 2, text: 'dentro', confidence: 0.9 },
    { seq: 3, start: 4, end: 5, text: 'depois', confidence: 0.9 }
  ];

  it('palavras com algum trecho na janela são mantidas; encostando na borda não entra', () => {
    expect(wordsInWindow(words, { start: 1, end: 4 }).map((w) => w.seq)).toEqual([2]);
    expect(wordsInWindow(words, { start: 0.5, end: 4.5 }).map((w) => w.seq)).toEqual([1, 2, 3]);
  });

  it('destaque: palavra que se sobrepõe ao intervalo', () => {
    expect(wordInInterval(words[1], { start: 1.5, end: 1.6 })).toBe(true);
    expect(wordInInterval(words[1], { start: 2, end: 3 })).toBe(false);
  });
});

describe('reviewModel: mensagens de erro', () => {
  it('códigos conhecidos do host têm texto PT-BR', () => {
    const err = Object.assign(new Error('bruto'), { code: 'NOTHING_TO_UNDO' });
    expect(describeDecisionError(err)).toEqual({ code: 'NOTHING_TO_UNDO', message: 'Nada para desfazer neste candidato.' });
  });

  it('código desconhecido mantém a mensagem recebida', () => {
    const err = Object.assign(new Error('falhou no host'), { code: 'XYZ' });
    expect(describeDecisionError(err)).toEqual({ code: 'XYZ', message: 'falhou no host' });
  });

  it('erro sem mensagem recebe texto padrão', () => {
    expect(describeDecisionError(42)).toEqual({ code: null, message: 'Não foi possível registrar a decisão.' });
  });

  it('planError PLAN_REGENERATION_FAILED tem mensagem própria (decisão continua gravada)', () => {
    expect(decisionPlanErrorMessage('PLAN_REGENERATION_FAILED')).toBe(
      'Decisão gravada, mas o plano não pôde ser atualizado — gere nova revisão na Tela 03.'
    );
    expect(decisionPlanErrorMessage('OUTRO')).toBe('Decisão gravada, mas o plano não foi regenerado (código OUTRO).');
  });
});
