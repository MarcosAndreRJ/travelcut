import { renderToString } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { PipelineStatusDto, WhisperExecutionDto } from '../../../types/ipc';
import { ExecutionDetails, PipelineTechnicalDetails, pickExecution } from '../PipelineTechnicalDetails';

const execution = (over: Partial<WhisperExecutionDto> = {}): WhisperExecutionDto => ({
  jobId: 'job-1',
  mediaId: 'media-1',
  requestId: 'req-1',
  mediaPath: 'K:\\Viagem São Tomé\\1-Bruto\\clipe ação.mp4',
  startedAtUtc: '2026-11-07T10:00:00Z',
  finishedAtUtc: null,
  pythonExecutable: 'C:\\Users\\u\\AppData\\Local\\TravelCut\\venv\\Scripts\\python.exe',
  arguments: ['K:\\App\\workers\\whisper_worker.py'],
  workerScript: 'K:\\App\\workers\\whisper_worker.py',
  workingDirectory: 'K:\\App',
  environmentVariables: { PYTHONUTF8: '1', PYTHONIOENCODING: 'utf-8' },
  standardInput: '{"command":"transcribe","requestId":"req-1"}\n{"command":"exit"}\n',
  model: 'large-v3',
  language: 'pt',
  device: 'cpu',
  processState: 'running',
  exitCode: null,
  canceled: false,
  error: null,
  logLines: [{ atUtc: '2026-11-07T10:00:05Z', stream: 'stderr', text: 'FP16 is not supported on CPU' }],
  droppedLogLines: 3,
  diagnosticScript: "& { $psi.FileName = 'python.exe' }",
  ...over
});

const NOW = Date.parse('2026-11-07T10:05:00Z');

describe('ExecutionDetails', () => {
  const html = renderToString(<ExecutionDetails execution={execution()} nowMs={NOW} />);

  it('mostra executável, argumentos, worker, diretório, ambiente, mídia, modelo, idioma e dispositivo reais', () => {
    expect(html).toContain('venv\\Scripts\\python.exe');
    expect(html).toContain('whisper_worker.py');
    expect(html).toContain('PYTHONUTF8=1');
    expect(html).toContain('clipe ação.mp4');
    expect(html).toContain('large-v3');
    expect(html).toContain('cpu');
    expect(html).toContain('Em execução');
    expect(html).toContain('5 min 00 s');
  });

  it('explica que a chamada vai por stdin e mostra o NDJSON', () => {
    expect(html).toContain('vão no stdin (NDJSON)');
    expect(html).toContain('&quot;command&quot;:&quot;transcribe&quot;');
  });

  it('mostra logs limitados, sem prometer texto transcrito', () => {
    expect(html).toContain('[<!-- -->stderr<!-- -->]');
    expect(html).toContain('3 mais antigas descartadas');
    expect(html).toContain('não é registrado nos logs');
  });

  it('o script só aparece depois da ação de copiar', () => {
    expect(html).toContain('Copiar comando de diagnóstico (PowerShell)');
    expect(html).not.toContain('$psi.FileName');
  });

  it('código de saída, cancelamento e erro', () => {
    const done = renderToString(
      <ExecutionDetails execution={execution({ processState: 'canceled', canceled: true, exitCode: 1, error: 'Transcrição cancelada.', finishedAtUtc: '2026-11-07T10:01:00Z' })} nowMs={NOW} />
    );
    expect(done).toContain('Cancelado');
    expect(done).toContain('Transcrição cancelada.');
    expect(done).toContain('1 min 00 s');
  });
});

describe('PipelineTechnicalDetails', () => {
  it('começa recolhido: nenhum caminho local é pedido nem exibido', () => {
    const status = { jobId: 'job-1', state: 'running', stages: [], media: [], currentMedia: null } as unknown as PipelineStatusDto;
    const html = renderToString(<PipelineTechnicalDetails projectId="p1" status={status} />);
    expect(html).toContain('Detalhes técnicos da execução');
    expect(html).toContain('aria-expanded="false"');
    expect(html).not.toContain('python');
  });

  it('escolhe a execução em andamento, senão a última', () => {
    const a = execution({ requestId: 'a', processState: 'completed' });
    const b = execution({ requestId: 'b', processState: 'running' });
    expect(pickExecution({ jobId: 'j', executions: [b, a] })?.requestId).toBe('b');
    expect(pickExecution({ jobId: 'j', executions: [a, { ...a, requestId: 'c' }] })?.requestId).toBe('c');
    expect(pickExecution({ jobId: 'j', executions: [] })).toBeNull();
  });
});
