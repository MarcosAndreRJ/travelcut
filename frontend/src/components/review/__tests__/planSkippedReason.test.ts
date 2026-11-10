import { describe, expect, it } from 'vitest';
import { describePlanSkippedReason } from '../reviewModel';

describe('describePlanSkippedReason', () => {
  it('PLAN_MANUAL_PROTECTED orienta a Timeline para reconciliar ou descartar', () => {
    const text = describePlanSkippedReason('PLAN_MANUAL_PROTECTED');
    expect(text).toBe(
      'Decisão registrada. O plano não foi atualizado porque há edições manuais protegidas — abra a Timeline e escolha reconciliar ou descartar a edição manual.'
    );
  });

  it('PLAN_MANUAL_EDITS_UNREADABLE informa que o plano foi preservado', () => {
    const text = describePlanSkippedReason('PLAN_MANUAL_EDITS_UNREADABLE');
    expect(text).toBe(
      'Decisão registrada. O plano não foi atualizado porque a revisão manual não pôde ser lida; o plano foi preservado.'
    );
  });

  it('código desconhecido cai em mensagem genérica (não nula)', () => {
    const text = describePlanSkippedReason('CODIGO_NOVO');
    expect(text).toBe('Decisão registrada. O plano não foi atualizado por causa de edições manuais protegidas.');
  });

  it('null, undefined e string vazia não geram aviso', () => {
    expect(describePlanSkippedReason(null)).toBeNull();
    expect(describePlanSkippedReason(undefined)).toBeNull();
    expect(describePlanSkippedReason('')).toBeNull();
  });
});
