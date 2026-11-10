import { describe, expect, it } from 'vitest';
import { afterHintOf, afterLabelOf, modeOptionsOf } from '../BeforeAfterCompare';

describe('rótulo do Depois conforme a fonte (M2)', () => {
  it('fonte original mostra "Original", nunca "Depois"', () => {
    const after = modeOptionsOf('original').find((o) => o.value === 'after');
    expect(after?.label).toBe('Original');
    expect(after?.label).not.toMatch(/^Depois/);
    expect(afterLabelOf('original')).toBe('Original');
  });

  it('fonte plano e candidato mantêm o rótulo "Depois"', () => {
    expect(modeOptionsOf('plan').find((o) => o.value === 'after')?.label).toBe('Depois (plano vigente)');
    expect(modeOptionsOf('candidate').find((o) => o.value === 'after')?.label).toBe('Depois (só este corte)');
  });

  it('dica do original não descreve pulo', () => {
    expect(afterHintOf('original')).not.toMatch(/Pula/);
    expect(afterHintOf('plan')).toMatch(/Pula/);
    expect(afterHintOf('candidate')).toMatch(/Pula/);
  });

  it('o modo Antes permanece fixo independentemente da fonte', () => {
    for (const source of ['plan', 'candidate', 'original'] as const) {
      expect(modeOptionsOf(source).find((o) => o.value === 'before')?.label).toBe('Antes');
    }
  });
});
