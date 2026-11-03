import { describe, expect, it } from 'vitest';
import type {
  AnalysisEnvironmentDto,
  EditorCapabilities,
  MotionCapabilitiesDto
} from '../../../types/ipc';
import {
  buildIntegrationRows,
  countByState,
  formatCounts,
  overallHeadline,
  type IntegrationInputs,
  type IntegrationRow
} from '../integrationStatus';

const ENV_OK: AnalysisEnvironmentDto = {
  ffmpegAvailable: true,
  ffmpegVersion: '7.1',
  pythonAvailable: true,
  whisperAvailable: true,
  whisperEngine: 'faster-whisper',
  whisperModel: 'large-v3',
  cudaAvailable: true,
  reasons: []
};

const ENV_EMPTY: AnalysisEnvironmentDto = {
  ffmpegAvailable: false,
  ffmpegVersion: null,
  pythonAvailable: false,
  whisperAvailable: false,
  whisperEngine: null,
  whisperModel: 'large-v3',
  cudaAvailable: null,
  reasons: ['FFmpeg não encontrado.']
};

function editor(overrides: Partial<EditorCapabilities>): EditorCapabilities {
  return {
    schemaVersion: 'editor/1',
    adapterMode: 'davinci',
    environmentState: 'READY',
    isAvailable: true,
    isSimulated: false,
    productName: 'DaVinci Resolve Studio',
    version: null,
    statusMessage: 'Resolve conectado.',
    reasonCode: null,
    operations: [],
    checkedAtUtc: '2026-11-03T00:00:00Z',
    canCreateProject: false,
    canImportMedia: false,
    canCreateTimeline: false,
    canApplyEditPlan: false,
    canExportPreview: false,
    ...overrides
  };
}

function motion(overrides: Partial<MotionCapabilitiesDto>): MotionCapabilitiesDto {
  return {
    schemaVersion: 'motion/1',
    adapterMode: 'hyperframes',
    environmentState: 'READY',
    isAvailable: true,
    isSimulated: false,
    nodeVersion: null,
    rendererVersion: null,
    statusMessage: 'CLI presente.',
    reasonCode: null,
    templates: [],
    checkedAtUtc: '2026-11-03T00:00:00Z',
    ...overrides
  };
}

function inputs(overrides: Partial<IntegrationInputs> = {}): IntegrationInputs {
  return {
    isMock: false,
    analysisEnv: ENV_OK,
    analysisError: null,
    editorCaps: editor({}),
    motionCaps: motion({}),
    motionError: null,
    ...overrides
  };
}

const byId = (rows: IntegrationRow[], id: IntegrationRow['id']): IntegrationRow => {
  const row = rows.find((r) => r.id === id);
  if (!row) throw new Error(`linha ausente: ${id}`);
  return row;
};

describe('buildIntegrationRows: sem leitura real vira "Não verificado"', () => {
  it('sem resposta do host, análise, DaVinci e animações ficam não verificados e sem versão', () => {
    const rows = buildIntegrationRows(inputs({ analysisEnv: null, editorCaps: null, motionCaps: null }));
    for (const id of ['davinci', 'ffmpeg', 'python', 'whisper', 'cuda', 'hyperframes'] as const) {
      const row = byId(rows, id);
      expect(row.state, id).toBe('unverified');
      expect(row.label, id).toBe('Não verificado');
      expect(row.version, id).toBeNull();
    }
  });

  it('o Claude Code (MCP) nunca é afirmado pelo app', () => {
    const row = byId(buildIntegrationRows(inputs()), 'claude');
    expect(row.state).toBe('unverified');
    expect(row.label).toBe('Não verificado pelo app');
  });

  it('fora do navegador, .NET e WebView2 aparecem em execução sem inventar versão', () => {
    const rows = buildIntegrationRows(inputs({ isMock: false }));
    expect(byId(rows, 'dotnet')).toMatchObject({ state: 'ready', label: 'Em execução', version: null });
    expect(byId(rows, 'webview2')).toMatchObject({ state: 'ready', label: 'Em execução', version: null });
  });

  it('no navegador (mock), .NET e WebView2 não são afirmados', () => {
    const rows = buildIntegrationRows(inputs({ isMock: true }));
    expect(byId(rows, 'dotnet').state).toBe('unverified');
    expect(byId(rows, 'webview2').state).toBe('unverified');
  });
});

describe('buildIntegrationRows: análise (analysis.environment)', () => {
  it('versão do FFmpeg só aparece quando o host informa', () => {
    expect(byId(buildIntegrationRows(inputs()), 'ffmpeg').version).toBe('7.1');
    const semVersao = buildIntegrationRows(inputs({ analysisEnv: { ...ENV_OK, ffmpegVersion: null } }));
    expect(byId(semVersao, 'ffmpeg')).toMatchObject({ state: 'ready', label: 'Disponível', version: null });
  });

  it('ausência do FFmpeg e do Whisper é "missing", nunca "ready"', () => {
    const rows = buildIntegrationRows(inputs({ analysisEnv: ENV_EMPTY }));
    expect(byId(rows, 'ffmpeg')).toMatchObject({ state: 'missing', label: 'Não encontrado' });
    expect(byId(rows, 'whisper')).toMatchObject({ state: 'missing', label: 'Não encontrado' });
    expect(byId(rows, 'python')).toMatchObject({ state: 'missing' });
  });

  it('CUDA: verdadeiro é GPU, falso é CPU limitada, nulo é não verificado', () => {
    expect(byId(buildIntegrationRows(inputs()), 'cuda')).toMatchObject({ state: 'ready', label: 'Disponível (GPU)' });
    expect(
      byId(buildIntegrationRows(inputs({ analysisEnv: { ...ENV_OK, cudaAvailable: false } })), 'cuda')
    ).toMatchObject({ state: 'limited', label: 'Indisponível (CPU)' });
    expect(
      byId(buildIntegrationRows(inputs({ analysisEnv: { ...ENV_OK, cudaAvailable: null } })), 'cuda').state
    ).toBe('unverified');
  });

  it('versão do Whisper vem do engine real e o modelo aparece no detalhe', () => {
    const row = byId(buildIntegrationRows(inputs()), 'whisper');
    expect(row.version).toBe('faster-whisper');
    expect(row.detail).toContain('large-v3');
  });

  it('falha na consulta vira erro visível, não "Não verificado" silencioso', () => {
    const rows = buildIntegrationRows(inputs({ analysisEnv: null, analysisError: 'Timeout ao aguardar' }));
    expect(byId(rows, 'ffmpeg')).toMatchObject({ state: 'error', detail: 'Timeout ao aguardar' });
    expect(byId(rows, 'whisper').state).toBe('error');
  });
});

describe('buildIntegrationRows: DaVinci (editor.capabilities)', () => {
  it('READY conecta, com versão só quando lida', () => {
    expect(byId(buildIntegrationRows(inputs()), 'davinci')).toMatchObject({ state: 'ready', label: 'Conectado', version: null });
    expect(
      byId(buildIntegrationRows(inputs({ editorCaps: editor({ version: '19.1.2' }) })), 'davinci').version
    ).toBe('19.1.2');
  });

  it('READY_PARTIAL é conectado com limitações', () => {
    expect(
      byId(buildIntegrationRows(inputs({ editorCaps: editor({ environmentState: 'READY_PARTIAL' }) })), 'davinci')
    ).toMatchObject({ state: 'limited', label: 'Conectado (limitado)' });
  });

  it('MOCK é simulador, nunca conectado', () => {
    const row = byId(
      buildIntegrationRows(inputs({ editorCaps: editor({ environmentState: 'MOCK', adapterMode: 'mock', isSimulated: true }) })),
      'davinci'
    );
    expect(row.state).toBe('simulated');
    expect(row.label).toBe('Simulador');
  });

  it('NOT_INSTALLED e outros estados sem conexão são ausentes', () => {
    const row = byId(
      buildIntegrationRows(inputs({ editorCaps: editor({ environmentState: 'NOT_INSTALLED', isAvailable: false }) })),
      'davinci'
    );
    expect(row).toMatchObject({ state: 'missing', label: 'Não encontrado' });
  });

  it('ERROR vira erro com a mensagem real do host', () => {
    const row = byId(
      buildIntegrationRows(inputs({ editorCaps: editor({ environmentState: 'ERROR', statusMessage: 'Falha na API.' }) })),
      'davinci'
    );
    expect(row).toMatchObject({ state: 'error', detail: 'Falha na API.' });
  });
});

describe('buildIntegrationRows: animações (motion.capabilities)', () => {
  it('READY só mostra rendererVersion quando lida', () => {
    expect(byId(buildIntegrationRows(inputs()), 'hyperframes')).toMatchObject({ state: 'ready', version: null });
    expect(
      byId(buildIntegrationRows(inputs({ motionCaps: motion({ rendererVersion: '1.4.2' }) })), 'hyperframes').version
    ).toBe('1.4.2');
  });

  it('MOCK é simulado e declara que não há vídeo', () => {
    const row = byId(
      buildIntegrationRows(inputs({ motionCaps: motion({ environmentState: 'MOCK', isSimulated: true }) })),
      'hyperframes'
    );
    expect(row).toMatchObject({ state: 'simulated', label: 'Simulador (sem vídeo)' });
  });

  it('NOT_INSTALLED e NODE_UNAVAILABLE são ausentes com motivo distinto', () => {
    expect(
      byId(buildIntegrationRows(inputs({ motionCaps: motion({ environmentState: 'NOT_INSTALLED' }) })), 'hyperframes').label
    ).toBe('Não instalado');
    expect(
      byId(buildIntegrationRows(inputs({ motionCaps: motion({ environmentState: 'NODE_UNAVAILABLE' }) })), 'hyperframes').label
    ).toBe('Node.js indisponível');
  });

  it('erro de consulta é visível', () => {
    expect(
      byId(buildIntegrationRows(inputs({ motionCaps: null, motionError: 'Sem resposta' })), 'hyperframes')
    ).toMatchObject({ state: 'error', detail: 'Sem resposta' });
  });
});

describe('resumo derivado (sem percentual fixo)', () => {
  it('contagens refletem apenas as linhas reais', () => {
    const rows = buildIntegrationRows(inputs({ analysisEnv: ENV_EMPTY, editorCaps: null }));
    const counts = countByState(rows);
    expect(counts.missing).toBe(3); // ffmpeg, python, whisper
    expect(counts.unverified).toBeGreaterThan(0);
    expect(formatCounts(rows)).toMatch(/ausentes/);
    expect(formatCounts(rows)).toMatch(/não verificados/);
  });

  it('formatCounts sem linhas informa que nada foi consultado', () => {
    expect(formatCounts([])).toBe('Nenhum item consultado.');
  });

  it('título geral: pronto só quando tudo verificado está pronto', () => {
    const pronto = buildIntegrationRows(inputs({ isMock: false }));
    // MCP fica fora da conta; DaVinci, análise e animações estão prontos.
    expect(overallHeadline(pronto).title).toBe('Ambiente pronto');
  });

  it('título geral aponta ausência, falha e verificação incompleta em ordem', () => {
    expect(overallHeadline(buildIntegrationRows(inputs({ analysisEnv: ENV_EMPTY })))).toMatchObject({
      title: 'Há itens ausentes'
    });
    expect(overallHeadline(buildIntegrationRows(inputs({ analysisEnv: null, analysisError: 'x' })))).toMatchObject({
      title: 'Há falhas na consulta ao host'
    });
    expect(overallHeadline(buildIntegrationRows(inputs({ analysisEnv: null, editorCaps: editor({}) })))).toMatchObject({
      title: 'Verificação incompleta'
    });
  });

  it('simulador ou conexão parcial é "ambiente com limitações"', () => {
    const rows = buildIntegrationRows(
      inputs({ editorCaps: editor({ environmentState: 'MOCK', adapterMode: 'mock', isSimulated: true }) })
    );
    expect(overallHeadline(rows).title).toBe('Ambiente com limitações');
  });
});

describe('ordem e identidade das linhas', () => {
  it('nenhum rótulo contém versões literais de exemplo (nada fixo)', () => {
    const rows = buildIntegrationRows(inputs({ analysisEnv: null, editorCaps: null, motionCaps: null }));
    const text = JSON.stringify(rows);
    for (const literal of ['v8.0.10', 'v126', 'v7.1.1', 'v1.4.2', 'RTX', '184.5', 'Windows 11']) {
      expect(text).not.toContain(literal);
    }
  });

  it('cada linha tem fonte técnica declarada', () => {
    for (const row of buildIntegrationRows(inputs())) {
      expect(row.source.length).toBeGreaterThan(0);
    }
  });
});
