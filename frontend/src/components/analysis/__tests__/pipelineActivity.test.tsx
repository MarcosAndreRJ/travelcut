import { renderToString } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { PipelineActivityDto, PipelineStatusDto } from '../../../types/ipc';
import { PipelineActivityCard } from '../PipelineActivitySummary';
import { activitySummaryLine, formatDuration, pipelineActivityView, STALE_TELEMETRY_SECONDS } from '../pipelineActivity';
import { shouldKeepPollingAfterError } from '../usePipeline';

const NOW = Date.parse('2026-11-07T12:00:00Z');
const ago = (seconds: number): string => new Date(NOW - seconds * 1000).toISOString();

const activity = (over: Partial<PipelineActivityDto> = {}): PipelineActivityDto => ({
  currentStage: 'transcription',
  currentStageLabel: 'Transcrição (Whisper large-v3)',
  detail: 'Transcrevendo',
  percent: null,
  lastActivityAtUtc: ago(5),
  cancelRequested: false,
  mediaCompleted: 0,
  mediaTotal: 1,
  ...over
});

const status = (over: Partial<PipelineStatusDto> = {}): PipelineStatusDto => ({
  jobId: 'job-1',
  projectId: 'p1',
  state: 'running',
  stages: [{ stage: 'transcription', label: 'Transcrição (Whisper large-v3)', state: 'running', processedItems: 0, totalItems: 1, reason: null }],
  media: [],
  currentMedia: '1-Bruto/Viagem/clipe longo.mp4',
  environment: null,
  startedAtUtc: ago(1800),
  finishedAtUtc: null,
  activity: activity(),
  ...over
});

const render = (s: PipelineStatusDto, hostUnreachable = false, nowMs = NOW): string =>
  renderToString(<PipelineActivityCard view={pipelineActivityView(s, nowMs)!} hostUnreachable={hostUnreachable} />);

describe('pipelineActivityView', () => {
  it('uma mídia há 30 minutos: etapa, nome, tempo e 0/1 sem parecer parada', () => {
    const view = pipelineActivityView(status(), NOW)!;
    expect(view.phase).toBe('active');
    expect(view.clipName).toBe('clipe longo.mp4');
    expect(view.stageLabel).toBe('Transcrição (Whisper large-v3)');
    expect(view.detail).toBe('Transcrevendo');
    expect(view.elapsedSeconds).toBe(1800);
    expect(view.mediaCompleted).toBe(0);
    expect(view.mediaTotal).toBe(1);
    expect(view.telemetry).toBe('recent');
  });

  it('sem percentual real o progresso é indeterminado, por mais tempo que passe', () => {
    expect(pipelineActivityView(status(), NOW)!.percent).toBeNull();
    expect(pipelineActivityView(status(), NOW + 3_600_000)!.percent).toBeNull();
  });

  it('aceita percentual só quando válido', () => {
    expect(pipelineActivityView(status({ activity: activity({ percent: 37.5 }) }), NOW)!.percent).toBe(37.5);
    expect(pipelineActivityView(status({ activity: activity({ percent: 140 }) }), NOW)!.percent).toBeNull();
    expect(pipelineActivityView(status({ activity: activity({ percent: -1 }) }), NOW)!.percent).toBeNull();
    expect(pipelineActivityView(status({ activity: activity({ percent: Number.NaN }) }), NOW)!.percent).toBeNull();
  });

  it('sinal antigo vira "sem telemetria recente", não falha', () => {
    const view = pipelineActivityView(
      status({ activity: activity({ lastActivityAtUtc: ago(STALE_TELEMETRY_SECONDS + 1) }) }),
      NOW
    )!;
    expect(view.telemetry).toBe('stale');
    expect(view.phase).toBe('active');
  });

  it('sem activity (demo) usa a etapa em execução e não finge telemetria', () => {
    const view = pipelineActivityView(status({ activity: null }), NOW)!;
    expect(view.stageLabel).toBe('Transcrição (Whisper large-v3)');
    expect(view.telemetry).toBe('unknown');
    expect(view.percent).toBeNull();
  });

  it('fila, cancelamento e fases finais', () => {
    expect(pipelineActivityView(status({ state: 'queued', startedAtUtc: null }), NOW)!.phase).toBe('waiting');
    expect(pipelineActivityView(status({ activity: activity({ cancelRequested: true }) }), NOW)!.phase).toBe('canceling');
    expect(pipelineActivityView(status(), NOW, true)!.phase).toBe('canceling');
    expect(pipelineActivityView(status({ state: 'canceled', finishedAtUtc: ago(0) }), NOW)!.phase).toBe('canceled');
    expect(pipelineActivityView(status({ state: 'idle', jobId: null }), NOW)).toBeNull();
    expect(pipelineActivityView(null, NOW)).toBeNull();
  });

  it('conclusão congela o tempo e limpa a etapa', () => {
    const view = pipelineActivityView(
      status({ state: 'completed', finishedAtUtc: ago(600), currentMedia: null, activity: activity({ mediaCompleted: 1, percent: 90 }) }),
      NOW
    )!;
    expect(view.phase).toBe('completed');
    expect(view.elapsedSeconds).toBe(1200);
    expect(view.stageLabel).toBeNull();
    expect(view.percent).toBeNull();
    expect(view.mediaCompleted).toBe(1);
  });

  it('falha traz o motivo real da etapa', () => {
    const view = pipelineActivityView(
      status({
        state: 'failed',
        finishedAtUtc: ago(1),
        stages: [{ stage: 'transcription', label: 'Transcrição', state: 'failed', processedItems: 1, totalItems: 1, reason: 'Modelo não encontrado.' }]
      }),
      NOW
    )!;
    expect(view.errorMessage).toBe('Modelo não encontrado.');
  });
});

describe('PipelineActivityCard', () => {
  it('mostra clipe, etapa, tempo, mídias e barra indeterminada', () => {
    const html = render(status());
    expect(html).toContain('clipe longo.mp4');
    expect(html).toContain('Transcrevendo');
    expect(html).toContain('30 min 00 s');
    expect(html).toContain('mídias concluídas');
    expect(html).toContain('o motor não informou percentual');
    expect(html).toContain('animate-pulse');
    expect(html).not.toContain('aria-valuenow');
  });

  it('com telemetria real mostra o percentual do motor', () => {
    const html = render(status({ activity: activity({ percent: 37.5 }) }));
    expect(html).toContain('37.5% desta etapa');
    expect(html).toContain('aria-valuenow="37.5"');
    expect(html).not.toContain('animate-pulse');
  });

  it('sinal antigo não diz que travou', () => {
    const html = render(status({ activity: activity({ lastActivityAtUtc: ago(300) }) }));
    expect(html).toContain('Sem telemetria recente');
    expect(html).toContain('não indica travamento');
  });

  it('falha temporária do host mantém o último estado', () => {
    const html = render(status(), true);
    expect(html).toContain('Sem resposta do host');
    expect(html).toContain('clipe longo.mp4');
  });

  it('cancelamento em andamento e estados finais', () => {
    expect(render(status({ activity: activity({ cancelRequested: true }) }))).toContain('Cancelando');
    expect(render(status({ state: 'canceled', finishedAtUtc: ago(0) }))).toContain('Cancelado');
    expect(render(status({ state: 'completed', finishedAtUtc: ago(0) }))).toContain('Concluído');
  });
  it('transicao de indeterminada para percentual real de 50.8% sem regressao ficticia', () => {
    // 1. Etapa indeterminada: nunca preenche 100%, sem aria-valuenow e usa animacao de segmento
    const indHtml = render(status({ activity: activity({ percent: null }) }));
    expect(indHtml).toContain('animate-indeterminate-slide');
    expect(indHtml).not.toContain('width: 100%');
    expect(indHtml).not.toContain('aria-valuenow');
    expect(indHtml).toContain('progresso desta etapa indisponível');

    // 2. Transicao para 50.8%: determinada com percentual real
    const detHtml = render(status({ activity: activity({ percent: 50.8 }) }));
    expect(detHtml).toContain('data-testid="determinate-bar"');
    expect(detHtml).toContain('width:50.8%');
    expect(detHtml).toContain('aria-valuenow="50.8"');
    expect(detHtml).toContain('50.8% desta etapa');
  });
});

describe('acompanhamento do pipeline', () => {
  it('falha temporária de IPC não encerra o polling de um job acompanhado', () => {
    expect(shouldKeepPollingAfterError({ jobId: 'job-1', sawActive: true, waitAttempts: 0 })).toBe(true);
    expect(shouldKeepPollingAfterError(null)).toBe(false);
  });

  it('resume a atividade para o painel do clipe', () => {
    const line = activitySummaryLine(pipelineActivityView(status({ activity: activity({ percent: 10 }) }), NOW));
    expect(line).toContain('Transcrevendo');
    expect(line).toContain('10.0%');
    expect(line).toContain('há 30 min 00 s');
  });

  it('formata durações', () => {
    expect(formatDuration(9)).toBe('9 s');
    expect(formatDuration(125)).toBe('2 min 05 s');
    expect(formatDuration(3900)).toBe('1 h 05 min');
  });
});
