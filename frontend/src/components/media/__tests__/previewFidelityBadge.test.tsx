import { describe, expect, it } from 'vitest';
import { renderToStaticMarkup } from 'react-dom/server';
import { FIDELITY_LABEL, PreviewFidelityBadge } from '../PreviewFidelityBadge';

describe('PreviewFidelityBadge (F14.8 item 3)', () => {
  it('mostra o texto exato da prévia HTML5 sem afirmar precisão de render', () => {
    const html = renderToStaticMarkup(<PreviewFidelityBadge fidelity="html5_approx" />);
    expect(html).toContain('Prévia aproximada (HTML5)');
    expect(html).toContain('data-fidelity="html5_approx"');
    expect(html.toLowerCase()).not.toMatch(/frame-accurate|precis[aã]o|idêntic|identico/);
  });

  it('rotula o render final como referência, não reproduzido na prévia', () => {
    expect(FIDELITY_LABEL.final_render).toBe('Render final: referência, não reproduzido aqui');
    expect(FIDELITY_LABEL.validated).toContain('sem reprodução');
  });

  it('exibe a limitação explícita quando o efeito não é reproduzível', () => {
    const html = renderToStaticMarkup(
      <PreviewFidelityBadge fidelity="html5_approx" limitation="Efeitos de transição não são reproduzidos aqui." />
    );
    expect(html).toContain('data-testid="preview-fidelity-limitation"');
    expect(html).toContain('Efeitos de transição não são reproduzidos aqui.');
  });

  it('sem limitação, não renderiza o aviso', () => {
    const html = renderToStaticMarkup(<PreviewFidelityBadge fidelity="html5_approx" limitation={null} />);
    expect(html).not.toContain('preview-fidelity-limitation');
  });
});
