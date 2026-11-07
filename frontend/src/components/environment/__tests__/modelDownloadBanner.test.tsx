import { renderToString } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { ToolInstallProgressDto } from '../../../types/ipc';
import type { ModelDownloadState } from '../../../services/modelDownloadTracker';
import { ModelDownloadBannerView, modelDownloadMetrics } from '../ModelDownloadBanner';
import { ModelDownloadModal } from '../ModelDownloadModal';

const progress = (over: Partial<ToolInstallProgressDto> = {}): ToolInstallProgressDto => ({
  jobId: 'job-1',
  toolKey: 'whisper',
  stage: 'Baixando Modelo',
  progressPercent: 42,
  currentMessage: 'Baixando large-v3...',
  outputLogs: [],
  canCancel: true,
  isCompleted: false,
  isSuccess: false,
  isFailed: false,
  isCanceled: false,
  ...over
});

const state = (over: Partial<ModelDownloadState>): ModelDownloadState => ({
  phase: 'running',
  jobId: 'job-1',
  modelName: 'large-v3',
  progress: progress(),
  speedBytesPerSecond: null,
  etaSeconds: null,
  pollFailures: 0,
  lastResponseAtMs: 1,
  errorMessage: null,
  ...over
});

const render = (s: ModelDownloadState): string =>
  renderToString(<ModelDownloadBannerView state={s} onCancel={() => undefined} onDismiss={() => undefined} />);

describe('ModelDownloadBannerView', () => {
  it('não renderiza nada em repouso', () => {
    expect(render(state({ phase: 'idle', progress: null }))).toBe('');
  });

  it('mostra aviso de confirmação nativa antes de existir job', () => {
    const html = render(state({ phase: 'confirming', jobId: null, progress: null }));
    expect(html).toContain('Aguardando confirmação');
    expect(html).toContain('janela do Windows');
    expect(html).not.toContain('Fechar');
  });

  it('mostra percentual, bytes, velocidade e tempo restante quando há dados reais', () => {
    const html = render(
      state({
        progress: progress({ bytesDownloaded: 1_048_576, totalBytes: 2_097_152 }),
        speedBytesPerSecond: 1_048_576,
        etaSeconds: 90
      })
    );
    expect(html).toContain('42');
    expect(html).toContain('role="progressbar"');
    expect(html).toContain('/s');
    expect(html).toContain('cerca de 2 min restantes');
  });

  it('sem percentual real a barra fica indeterminada e não mostra número', () => {
    const html = render(state({ progress: progress({ progressPercent: null, stage: 'Verificando SHA-256' }) }));
    expect(html).toContain('Verificando SHA-256');
    expect(html).toContain('em andamento');
    expect(html).toContain('animate-pulse');
    expect(html).not.toContain('aria-valuenow');
  });

  it('conclusão tem confirmação explícita e pode ser fechada', () => {
    const html = render(
      state({
        phase: 'success',
        progress: progress({
          isCompleted: true,
          isSuccess: true,
          stage: 'Concluído',
          progressPercent: 100,
          canCancel: false,
          currentMessage: 'Modelo large-v3 baixado e validado com sucesso com hash SHA-256 oficial!'
        })
      })
    );
    expect(html).toContain('validado com sucesso');
    expect(html).toContain('Diagnóstico do ambiente atualizado');
    expect(html).toContain('Fechar');
    expect(html).not.toContain('Cancelar');
  });

  it('cancelamento solicitado e confirmado são diferentes', () => {
    expect(render(state({ phase: 'canceling' }))).toContain('Cancelamento solicitado');
    const html = render(
      state({ phase: 'canceled', progress: progress({ isCompleted: true, isCanceled: true, stage: 'Cancelado' }) })
    );
    expect(html).toContain('cancelada pelo usuário');
  });

  it('falha mostra a mensagem do host', () => {
    const html = render(
      state({
        phase: 'failed',
        errorMessage: 'Hash inválido',
        progress: progress({ isCompleted: true, isFailed: true, stage: 'Erro', errorMessage: 'Hash inválido' })
      })
    );
    expect(html).toContain('Hash inválido');
  });

  it('falha ao iniciar, sem job, também é visível', () => {
    expect(render(state({ phase: 'failed', jobId: null, progress: null, errorMessage: 'Sem espaço' }))).toContain('Sem espaço');
  });

  it('job perdido é falha visível', () => {
    expect(render(state({ phase: 'lost', errorMessage: 'O host não reconhece mais este download.' }))).toContain('não reconhece');
  });
});

describe('modelDownloadMetrics', () => {
  it('não inventa total, velocidade nem tempo restante', () => {
    const lines = modelDownloadMetrics(state({ progress: progress({ bytesDownloaded: 2048, totalBytes: null }) }));
    expect(lines).toHaveLength(1);
    expect(lines[0]).toContain('baixados');
  });

  it('informa falta de resposta do host sem encerrar', () => {
    expect(modelDownloadMetrics(state({ pollFailures: 2 })).join(' ')).toContain('sem resposta do host');
  });
});

describe('ModelDownloadModal', () => {
  const html = renderToString(<ModelDownloadModal isOpen onConfirm={() => undefined} onClose={() => undefined} />);

  it('descreve o formato e a origem reais', () => {
    expect(html).toContain('Checkpoint PyTorch (.pt)');
    expect(html).toContain('openaipublic.azureedge.net');
  });

  it('não anuncia formatos ou recursos que o motor não usa', () => {
    expect(html).not.toContain('GGML');
    expect(html).not.toContain('Safetensors');
    expect(html).not.toContain('HuggingFace');
    expect(html).not.toContain('int8');
  });
});
