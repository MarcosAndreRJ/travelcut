import React from 'react';

/**
 * Nível de fidelidade declarado de uma prévia. Nunca afirmar precisão de render:
 * - html5_approx: reprodução HTML5 no WebView2, aproximada (não frame-accurate).
 * - validated: valores conferidos contra dados do host, sem reproduzir o vídeo.
 * - final_render: render final do DaVinci/export, é a referência e não é reproduzido aqui.
 */
export type PreviewFidelity = 'html5_approx' | 'validated' | 'final_render';

export const FIDELITY_LABEL: Record<PreviewFidelity, string> = {
  html5_approx: 'Prévia aproximada (HTML5)',
  validated: 'Valores validados pelo host (sem reprodução)',
  final_render: 'Render final: referência, não reproduzido aqui'
};

interface PreviewFidelityBadgeProps {
  fidelity: PreviewFidelity;
  /** Efeito que a prévia não reproduz (ex.: transição fade). Exibido ao lado do selo quando presente. */
  limitation?: string | null;
}

/** Selo de fidelidade da prévia. Texto fixo por nível; limitação explícita quando o efeito não é reproduzível. */
export const PreviewFidelityBadge: React.FC<PreviewFidelityBadgeProps> = ({ fidelity, limitation = null }) => (
  <span className="inline-flex flex-wrap items-center gap-1.5" data-testid="preview-fidelity-badge" data-fidelity={fidelity}>
    <span className="px-2 py-0.5 rounded text-[10px] font-semibold bg-amber-950/50 border border-amber-500/30 text-amber-200">
      {FIDELITY_LABEL[fidelity]}
    </span>
    {limitation && (
      <span role="note" data-testid="preview-fidelity-limitation" className="text-[10px] text-amber-300">
        {limitation}
      </span>
    )}
  </span>
);
