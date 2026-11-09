import { describe, it, expect } from 'vitest';
import React from 'react';
import { renderToString } from 'react-dom/server';
import { AutomationPreferencesPanel, AUTOMATION_PROFILE_OPTIONS } from '../AutomationPreferencesPanel';

describe('AutomationPreferencesPanel', () => {
  it('oferece os tres perfis com uma descricao cada', () => {
    expect(AUTOMATION_PROFILE_OPTIONS.map((o) => o.value)).toEqual(['conservative', 'balanced', 'aggressive']);
    expect(AUTOMATION_PROFILE_OPTIONS.map((o) => o.label)).toEqual(['Conservador', 'Equilibrado', 'Agressivo']);
    for (const option of AUTOMATION_PROFILE_OPTIONS) {
      expect(option.description.length).toBeGreaterThan(20);
      expect(option.description.split('.').filter((s) => s.trim().length > 0)).toHaveLength(1);
    }
  });

  it('renderiza estado de carregamento antes de ler o host (sem perfil fake)', () => {
    const html = renderToString(<AutomationPreferencesPanel projectId="p-1" />);
    expect(html).toContain('Carregando preferências de automação');
    expect(html).not.toContain('PLAN_OUTDATED');
    expect(html).not.toContain('role="radio"');
  });
});
