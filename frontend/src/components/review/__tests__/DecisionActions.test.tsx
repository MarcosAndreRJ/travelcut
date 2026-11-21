import { renderToString } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { EditSuggestion } from '../../../types/ipc';
import { DecisionActions } from '../DecisionActions';

const base = { suggestionId: 's1', mediaId: 'm1', kind: 'silence', startSeconds: 1, endSeconds: 2 } as EditSuggestion;

const render = (status: string) =>
  renderToString(
    <DecisionActions
      suggestion={{ ...base, status } as EditSuggestion}
      busy={false}
      modifyOpen={false}
      onApprove={() => undefined}
      onReject={() => undefined}
      onDefer={() => undefined}
      onReopen={() => undefined}
      onToggleModify={() => undefined}
      onUndo={() => undefined}
      onRedo={() => undefined}
    />
  );

describe('DecisionActions: rótulo de restauração', () => {
  it('sugestão de corte aprovada mostra "Restaurar corte" no lugar de Rejeitar', () => {
    const html = render('Approved');
    expect(html).toContain('Restaurar corte');
    expect(html).not.toContain('Rejeitar');
  });

  it('sugestão pendente mantém "Rejeitar"', () => {
    const html = render('Proposed');
    expect(html).toContain('Rejeitar');
    expect(html).not.toContain('Restaurar corte');
  });
});
