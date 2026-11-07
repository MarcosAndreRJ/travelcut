import React, { useCallback, useEffect, useState } from 'react';
import { ChevronDown, ChevronRight, Copy, RefreshCw } from 'lucide-react';
import type { PipelineDiagnosticsDto, PipelineStatusDto, WhisperExecutionDto } from '../../types/ipc';
import { ipc } from '../../services/ipcBridge';
import { errorMessage, isPipelineActive } from './analysisFormat';
import { formatDuration, pipelineActivityView } from './pipelineActivity';

const PROCESS_STATE_LABEL: Record<string, string> = {
  running: 'Em execução',
  completed: 'Concluído (resposta válida do worker)',
  failed: 'Falhou',
  canceled: 'Cancelado',
  timed_out: 'Tempo limite excedido',
  not_started: 'Processo não iniciado'
};

const POLL_MS = 2000;

const localTime = (iso: string | null): string => (iso ? new Date(iso).toLocaleString('pt-BR') : '—');

/** Execução mais relevante: a em andamento ou a última. */
export function pickExecution(diagnostics: PipelineDiagnosticsDto | null): WhisperExecutionDto | null {
  if (!diagnostics || diagnostics.executions.length === 0) return null;
  return diagnostics.executions.find((e) => e.processState === 'running') ?? diagnostics.executions[diagnostics.executions.length - 1];
}

const Field: React.FC<{ label: string; children: React.ReactNode }> = ({ label, children }) => (
  <div className="grid grid-cols-[9rem_1fr] gap-2">
    <dt className="text-slate-500">{label}</dt>
    <dd className="text-slate-200 break-all">{children}</dd>
  </div>
);

export const ExecutionDetails: React.FC<{ execution: WhisperExecutionDto; nowMs: number }> = ({ execution, nowMs }) => {
  const [scriptVisible, setScriptVisible] = useState(false);
  const [copyMessage, setCopyMessage] = useState<string | null>(null);

  const started = execution.startedAtUtc ? Date.parse(execution.startedAtUtc) : null;
  const ended = execution.finishedAtUtc ? Date.parse(execution.finishedAtUtc) : nowMs;
  const elapsed = started !== null ? Math.max(0, Math.floor((ended - started) / 1000)) : null;
  const env = Object.entries(execution.environmentVariables);

  const copyScript = async () => {
    setScriptVisible(true);
    try {
      await navigator.clipboard.writeText(execution.diagnosticScript);
      setCopyMessage('Comando copiado. Cole no Windows PowerShell; nada é executado automaticamente.');
    } catch {
      setCopyMessage('Não foi possível copiar automaticamente: selecione o texto abaixo e copie.');
    }
  };

  return (
    <div className="space-y-2">
      <dl className="space-y-0.5 text-[10px] font-mono">
        <Field label="Estado do processo">{PROCESS_STATE_LABEL[execution.processState] ?? execution.processState}</Field>
        <Field label="Job / mídia">
          {execution.jobId} / {execution.mediaId}
        </Field>
        <Field label="Início">{localTime(execution.startedAtUtc)}</Field>
        <Field label="Conclusão">{localTime(execution.finishedAtUtc)}</Field>
        <Field label="Tempo decorrido">{elapsed !== null ? formatDuration(elapsed) : '—'}</Field>
        <Field label="Código de saída">{execution.exitCode ?? '—'}</Field>
        <Field label="Cancelado">{execution.canceled ? 'sim' : 'não'}</Field>
        <Field label="Python">{execution.pythonExecutable}</Field>
        <Field label="Argumentos">{execution.arguments.map((a) => JSON.stringify(a)).join(' ')}</Field>
        <Field label="Worker">{execution.workerScript}</Field>
        <Field label="Diretório de trabalho">{execution.workingDirectory}</Field>
        <Field label="Ambiente definido">{env.length > 0 ? env.map(([k, v]) => `${k}=${v}`).join('  ') : '—'}</Field>
        <Field label="Mídia">{execution.mediaPath}</Field>
        <Field label="Modelo / idioma">
          {execution.model} / {execution.language || 'auto'}
        </Field>
        <Field label="Dispositivo detectado">{execution.device ?? 'não informado'}</Field>
        <Field label="Stdin (NDJSON)">
          <pre className="whitespace-pre-wrap text-slate-300">{execution.standardInput.trim()}</pre>
        </Field>
        {execution.error && <Field label="Erro">{execution.error}</Field>}
      </dl>
      <p className="text-[10px] text-slate-500">
        A mídia e o modelo vão no stdin (NDJSON), não nos argumentos de linha de comando.
      </p>

      <div>
        <p className="text-[10px] font-semibold text-slate-300 mb-1">
          Logs técnicos ({execution.logLines.length} linhas{execution.droppedLogLines > 0 ? `, ${execution.droppedLogLines} mais antigas descartadas` : ''})
        </p>
        <div className="max-h-40 overflow-y-auto rounded-lg bg-black/60 border border-slate-800 p-2 font-mono text-[10px] space-y-0.5">
          {execution.logLines.length === 0 ? (
            <p className="text-slate-500">Nenhuma linha registrada até agora.</p>
          ) : (
            execution.logLines.map((line, idx) => (
              <div key={idx} className="whitespace-pre-wrap break-all">
                <span className={line.stream === 'stderr' ? 'text-amber-400' : 'text-cyan-400'}>[{line.stream}]</span>{' '}
                <span className="text-slate-300">{line.text}</span>
              </div>
            ))
          )}
        </div>
        <p className="text-[10px] text-slate-500 mt-1">O texto transcrito não é registrado nos logs técnicos.</p>
      </div>

      <div className="space-y-1">
        <button
          type="button"
          onClick={() => void copyScript()}
          className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 border border-slate-700 text-[11px] font-semibold text-slate-200 flex items-center gap-1.5"
        >
          <Copy className="w-3.5 h-3.5" aria-hidden="true" /> Copiar comando de diagnóstico (PowerShell)
        </button>
        {copyMessage && <p role="status" className="text-[10px] text-slate-300">{copyMessage}</p>}
        {scriptVisible && (
          <textarea
            readOnly
            aria-label="Script de diagnóstico PowerShell"
            value={execution.diagnosticScript}
            className="w-full h-40 rounded-lg bg-black/60 border border-slate-800 p-2 font-mono text-[10px] text-slate-300"
          />
        )}
      </div>
    </div>
  );
};

/** Seção recolhida: os caminhos locais só são pedidos ao host quando o usuário a abre. */
export const PipelineTechnicalDetails: React.FC<{ projectId: string; status: PipelineStatusDto }> = ({ projectId, status }) => {
  const [open, setOpen] = useState(false);
  const [diagnostics, setDiagnostics] = useState<PipelineDiagnosticsDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [nowMs, setNowMs] = useState(() => Date.now());
  const active = isPipelineActive(status.state);

  const load = useCallback(async () => {
    try {
      setDiagnostics(await ipc.getPipelineDiagnostics(projectId));
      setError(null);
    } catch (err) {
      setError(errorMessage(err));
    }
    setNowMs(Date.now());
  }, [projectId]);

  useEffect(() => {
    if (!open) return;
    void load();
    if (!active) return;
    const timer = setInterval(() => void load(), POLL_MS);
    return () => clearInterval(timer);
  }, [open, active, load, status.jobId]);

  const view = pipelineActivityView(status, nowMs);
  const execution = pickExecution(diagnostics);

  return (
    <div className="mb-2 rounded-xl border border-slate-800/80 bg-[#0a1222]">
      <button
        type="button"
        aria-expanded={open}
        onClick={() => setOpen((v) => !v)}
        className="w-full px-3 py-1.5 flex items-center gap-1.5 text-[11px] font-semibold text-slate-300 hover:text-white"
      >
        {open ? <ChevronDown className="w-3.5 h-3.5" aria-hidden="true" /> : <ChevronRight className="w-3.5 h-3.5" aria-hidden="true" />}
        Detalhes técnicos da execução
        {open && (
          <RefreshCw
            className="w-3 h-3 ml-auto text-slate-500"
            aria-label="Atualizar detalhes"
            onClick={(event) => {
              event.stopPropagation();
              void load();
            }}
          />
        )}
      </button>
      {open && (
        <div className="px-3 pb-3 space-y-2 text-[11px]">
          {view && (
            <p className="text-slate-300">
              {view.phaseLabel}
              {view.stageLabel ? ` · ${view.stageLabel}` : ''}
              {view.detail ? ` · ${view.detail}` : ''}
              {view.elapsedSeconds !== null ? ` · ${formatDuration(view.elapsedSeconds)}` : ''}
              {view.lastActivitySecondsAgo !== null ? ` · último sinal há ${formatDuration(view.lastActivitySecondsAgo)}` : ''}
            </p>
          )}
          {view && (view.phase === 'active' || view.phase === 'canceling') && view.percent === null && (
            <p className="text-slate-400">A transcrição está em execução sem percentual interno disponível.</p>
          )}
          {error && <p role="alert" className="text-rose-300">Não foi possível ler os detalhes: {error}</p>}
          {!error && diagnostics && !execution && (
            <p className="text-slate-500">
              Nenhuma execução do worker Whisper registrada neste job
              {ipc.isMock ? ' (modo demonstração: sem processo real).' : '. O processo pode não ter sido iniciado (veja o motivo na etapa).'}
            </p>
          )}
          {execution && <ExecutionDetails execution={execution} nowMs={nowMs} />}
        </div>
      )}
    </div>
  );
};
