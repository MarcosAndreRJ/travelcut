import React from 'react';

const SHORTCUTS: { keys: string[]; label: string }[] = [
  { keys: ['A'], label: 'Aprovar' },
  { keys: ['R'], label: 'Rejeitar' },
  { keys: ['D'], label: 'Adiar' },
  { keys: ['M'], label: 'Modificar' },
  { keys: ['Z'], label: 'Desfazer última decisão' },
  { keys: ['Y'], label: 'Refazer (reaplica a desfeita)' },
  { keys: ['↑', '↓'], label: 'Navegar' },
  { keys: ['Esc'], label: 'Fechar editor/confirmação' }
];

/** Legenda dos atalhos. Atalhos de letra só atuam fora de campos de texto. */
export const ShortcutLegend: React.FC = () => (
  <section aria-label="Atalhos de teclado" className="border-t border-slate-800 bg-[#0a1222] px-3 py-2">
    <h3 className="text-[10px] font-bold uppercase tracking-wider text-slate-400 mb-1.5">Atalhos</h3>
    <ul className="flex flex-wrap gap-x-3 gap-y-1 text-[10px] text-slate-400">
      {SHORTCUTS.map((s) => (
        <li key={s.label} className="flex items-center gap-1">
          <span className="flex gap-0.5">
            {s.keys.map((k) => (
              <kbd key={k} className="min-w-[16px] text-center px-1 rounded bg-slate-900 border border-slate-700 font-mono text-[9px] text-slate-200">
                {k}
              </kbd>
            ))}
          </span>
          <span>{s.label}</span>
        </li>
      ))}
    </ul>
    <p className="mt-1.5 text-[9px] text-slate-500">Letras não atuam com campo de texto em foco.</p>
  </section>
);
