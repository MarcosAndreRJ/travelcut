import React, { useState } from 'react';
import {
  CheckCircle2,
  AlertCircle,
  RefreshCw,
  FileText,
  ArrowRight,
  ExternalLink,
  Lightbulb,
  Server,
  Layers
} from 'lucide-react';
import { EditorCapabilities } from '../types/ipc';
import { ipc } from '../services/ipcBridge';
import { EditorBadge } from './editor/EditorPrimitives';
import { isEditorConnected } from './editor/editorModel';
import {
  buildIntegrationRows,
  countByState,
  formatCounts,
  overallHeadline,
  type IntegrationRow
} from './environment/integrationStatus';
import { useIntegrationStatus } from './environment/useIntegrationStatus';

interface EnvironmentCheckViewProps {
  editorCaps: EditorCapabilities | null;
  onRefresh: () => void | Promise<void>;
  onContinue: () => void;
}

const DOC_NOTE = 'Documentação no pacote: LEIA-ME-HOST.md';

export const EnvironmentCheckView: React.FC<EnvironmentCheckViewProps> = ({
  editorCaps,
  onRefresh,
  onContinue
}) => {
  const status = useIntegrationStatus();
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [showReasons, setShowReasons] = useState<boolean>(false);
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false);
  const isMock = ipc.isMock;

  // Todas as linhas vêm do host (ou ficam "Não verificado"); nenhum valor fixo.
  const rows = buildIntegrationRows({
    isMock,
    analysisEnv: status.analysisEnv,
    analysisError: status.analysisError,
    editorCaps,
    motionCaps: status.motionCaps,
    motionError: status.motionError
  });
  const headline = overallHeadline(rows);
  const counts = countByState(rows);
  const davinci: IntegrationRow | undefined = rows.find((r) => r.id === 'davinci');
  const davinciConnected = isEditorConnected(editorCaps);

  // Motivos reais reportados pelo host (sem texto de log inventado).
  const reasonLines: string[] = [
    ...(status.analysisEnv?.reasons ?? []),
    ...(editorCaps?.reasonCode ? [`DaVinci (${editorCaps.environmentState}): ${editorCaps.reasonCode}`] : []),
    ...(status.motionCaps?.reasonCode ? [`HyperFrames: ${status.motionCaps.reasonCode}`] : [])
  ];

  const handleRefresh = async () => {
    setIsRefreshing(true);
    try {
      await Promise.all([status.refresh(), Promise.resolve(onRefresh())]);
    } finally {
      setIsRefreshing(false);
    }
  };

  const selectedRow = rows.find((r) => r.id === selectedId);

  return (
    <div className="flex-1 overflow-y-auto bg-[#070d18] text-slate-100 p-8 space-y-8 font-sans">
      {/* Top Header & Breadcrumb */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <div className="text-xs font-semibold text-cyan-400 mb-1 flex items-center gap-1.5">
            <span>TravelCut Studio</span>
            <span className="text-slate-600">/</span>
            <span>Preparação</span>
            {isMock && (
              <span className="ml-2 px-1.5 py-0.5 rounded bg-amber-500/10 text-amber-300 border border-amber-500/30 text-[10px] font-bold uppercase tracking-wider">
                DEMONSTRAÇÃO
              </span>
            )}
          </div>
          <h1 className="text-2xl font-bold text-white tracking-tight">
            Verificação do ambiente
          </h1>
          <p className="text-xs text-slate-400 mt-0.5">
            Checagem das integrações e pré-requisitos antes de iniciar a edição.
          </p>
        </div>

        <div className="flex items-center gap-2.5">
          <button
            onClick={handleRefresh}
            disabled={isRefreshing || status.loading}
            className="px-3.5 py-2 rounded-xl bg-slate-900 hover:bg-slate-800 border border-slate-700/80 text-xs font-semibold text-slate-200 transition-all flex items-center gap-2 disabled:opacity-60"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isRefreshing || status.loading ? 'animate-spin text-cyan-400' : ''}`} />
            Atualizar verificações
          </button>
          <button
            onClick={() => setShowReasons((v) => !v)}
            aria-expanded={showReasons}
            className="px-3.5 py-2 rounded-xl bg-slate-900 hover:bg-slate-800 border border-slate-700/80 text-xs font-semibold text-slate-200 transition-all flex items-center gap-2"
          >
            <FileText className="w-3.5 h-3.5" />
            Ver motivos reportados
          </button>
          <button
            onClick={onContinue}
            className="px-4 py-2 rounded-xl bg-gradient-to-r from-cyan-500 to-blue-600 hover:from-cyan-400 hover:to-blue-500 text-xs font-bold text-white transition-all shadow-md shadow-cyan-950 flex items-center gap-2"
          >
            <CheckCircle2 className="w-3.5 h-3.5" />
            Continuar mesmo assim
          </button>
        </div>
      </div>

      {/* 3-Step Wizard Navigation */}
      <div className="p-4 rounded-2xl bg-[#0a1222] border border-slate-800 flex items-center justify-between text-xs">
        <div className="flex items-center gap-3">
          <div className="w-7 h-7 rounded-full bg-cyan-500 text-slate-950 font-black flex items-center justify-center text-xs shadow-md shadow-cyan-500/30">
            1
          </div>
          <div>
            <div className="font-bold text-white leading-tight">1. Verificação do ambiente</div>
            <div className="text-[11px] text-cyan-400 leading-tight">Checando integrações e pré-requisitos</div>
          </div>
        </div>

        <div className="h-0.5 flex-1 mx-6 bg-slate-800 hidden sm:block" />

        <div className="flex items-center gap-3 opacity-60">
          <div className="w-7 h-7 rounded-full bg-slate-800 text-slate-400 font-bold flex items-center justify-center text-xs">
            2
          </div>
          <div>
            <div className="font-bold text-slate-300 leading-tight">2. Preparar projeto</div>
            <div className="text-[11px] text-slate-500 leading-tight">Organizar mídia e pastas</div>
          </div>
        </div>

        <div className="h-0.5 flex-1 mx-6 bg-slate-800 hidden sm:block" />

        <div className="flex items-center gap-3 opacity-60">
          <div className="w-7 h-7 rounded-full bg-slate-800 text-slate-400 font-bold flex items-center justify-center text-xs">
            3
          </div>
          <div>
            <div className="font-bold text-slate-300 leading-tight">3. Iniciar montagem/edição</div>
            <div className="text-[11px] text-slate-500 leading-tight">Abrir o DaVinci e configurar timeline</div>
          </div>
        </div>
      </div>

      {/* Resumo derivado das leituras reais */}
      <div className="p-6 rounded-2xl bg-gradient-to-r from-[#0d1d35] via-[#0b172a] to-[#0a1424] border border-cyan-500/40 shadow-xl flex flex-col md:flex-row items-center justify-between gap-6">
        <div className="flex items-center gap-5">
          <div className="w-12 h-12 rounded-2xl bg-cyan-950/60 border border-cyan-500/30 flex items-center justify-center shrink-0">
            {headline.tone === 'emerald' ? (
              <CheckCircle2 className="w-6 h-6 text-emerald-400" />
            ) : (
              <AlertCircle className="w-6 h-6 text-amber-300" />
            )}
          </div>
          <div>
            <h2 className="text-lg font-bold text-white" role="status">
              {headline.title}
            </h2>
            <p className="text-xs text-slate-300 mt-0.5">{formatCounts(rows)}</p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <div className="px-3 py-1.5 rounded-xl bg-emerald-950/60 border border-emerald-500/30 text-emerald-300 text-xs font-semibold flex items-center gap-1.5">
            <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" />
            <span>{counts.ready} prontos</span>
          </div>
          <div className="px-3 py-1.5 rounded-xl bg-rose-950/60 border border-rose-500/30 text-rose-300 text-xs font-semibold flex items-center gap-1.5">
            <AlertCircle className="w-3.5 h-3.5 text-rose-400" />
            <span>{counts.missing + counts.error} ausentes ou com falha</span>
          </div>
          <div className="px-3 py-1.5 rounded-xl bg-slate-800/80 border border-slate-700 text-slate-300 text-xs font-semibold flex items-center gap-1.5">
            <Layers className="w-3.5 h-3.5 text-slate-400" />
            <span>{counts.unverified} não verificados</span>
          </div>
        </div>
      </div>

      {/* Motivos reais reportados pelo host */}
      {showReasons && (
        <div className="p-4 rounded-2xl bg-[#0a1222] border border-cyan-500/30 text-xs text-slate-300 space-y-2">
          <div className="font-bold text-cyan-400">Motivos reportados pelo host</div>
          {reasonLines.length > 0 ? (
            <ul className="list-disc pl-5 space-y-1">
              {reasonLines.map((line, index) => (
                <li key={`${index}-${line}`}>{line}</li>
              ))}
            </ul>
          ) : (
            <div>Nenhum motivo informado pelo host.</div>
          )}
        </div>
      )}

      {/* Main Grid */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Checklist real */}
        <div className="lg:col-span-2 rounded-2xl bg-[#0a1222] border border-slate-800 p-6">
          <div className="mb-4">
            <h3 className="text-base font-bold text-white">
              Verificações de integrações e pré-requisitos
            </h3>
            <p className="text-xs text-slate-400">
              Estado reportado pelo host. &quot;Não verificado&quot; significa que esta tela não obteve leitura real.
            </p>
          </div>

          <div className="divide-y divide-slate-800/80">
            {rows.map((item) => (
              <div key={item.id} className="py-3.5 flex items-center justify-between gap-4">
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2 mb-1">
                    <span className="text-xs font-bold text-white">{item.name}</span>
                    <EditorBadge badge={{ label: item.label, tone: item.tone }} />
                    {item.version && (
                      <span className="text-[10px] font-mono text-slate-400">{item.version}</span>
                    )}
                  </div>
                  <p className="text-[11px] text-slate-400 leading-snug">{item.detail}</p>
                </div>

                <button
                  onClick={() => setSelectedId(selectedId === item.id ? null : item.id)}
                  aria-expanded={selectedId === item.id}
                  className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-[11px] font-medium text-slate-300 shrink-0 transition-colors"
                >
                  {selectedId === item.id ? 'Fechar' : 'Detalhes'}
                </button>
              </div>
            ))}
          </div>

          {selectedRow && (
            <div className="mt-4 p-4 rounded-xl bg-slate-900 border border-cyan-500/30 text-xs text-slate-300 space-y-1">
              <div className="font-bold text-cyan-400 mb-1">Detalhes técnicos: {selectedRow.name}</div>
              <div>Estado: {selectedRow.label}</div>
              <div>Versão: {selectedRow.version ?? 'não informada pelo host'}</div>
              <div>Fonte: {selectedRow.source}</div>
              <div>{selectedRow.detail}</div>
            </div>
          )}
        </div>

        {/* Coluna direita: modo de execução e próximos passos */}
        <div className="space-y-6">
          <div className="rounded-2xl bg-[#0a1222] border border-slate-800 p-5 space-y-4">
            <h4 className="text-xs font-bold uppercase tracking-wider text-slate-400 flex items-center gap-2">
              <Server className="w-4 h-4 text-cyan-400" /> Modo de execução
            </h4>
            <div className="p-3.5 rounded-xl bg-[#080d19] border border-slate-800 text-xs text-slate-300">
              {isMock
                ? 'Demonstração no navegador: nenhum motor real é consultado.'
                : 'Aplicativo em execução com WebView2. Os estados acima vêm do host.'}
            </div>
          </div>

          <div className="rounded-2xl bg-[#0a1222] border border-slate-800 p-5 space-y-4">
            <h4 className="text-xs font-bold uppercase tracking-wider text-slate-400">
              Resumo e próximos passos
            </h4>

            {!davinciConnected && (
              <div className="p-3 rounded-xl bg-amber-950/30 border border-amber-500/30 text-amber-200 text-xs leading-relaxed">
                A edição pode continuar com limitações: o DaVinci Resolve não está conectado
                ({davinci?.label ?? 'não verificado'}). Análise e planejamento seguem disponíveis;
                montagem e exportação dependem do DaVinci.
              </div>
            )}

            <div className="space-y-3 text-xs">
              {!davinciConnected && (
                <div className="flex gap-2.5">
                  <span className="w-5 h-5 rounded-full bg-cyan-500/20 text-cyan-400 font-bold text-xs flex items-center justify-center shrink-0">
                    1
                  </span>
                  <div>
                    <div className="font-semibold text-slate-200">Abrir ou instalar o DaVinci Resolve Studio</div>
                    <div className="text-[11px] text-slate-400 mt-0.5 leading-snug">
                      Depois, execute uma nova verificação para confirmar o estado reportado pelo host.
                    </div>
                  </div>
                </div>
              )}

              <div className="flex gap-2.5">
                <span className="w-5 h-5 rounded-full bg-cyan-500/20 text-cyan-400 font-bold text-xs flex items-center justify-center shrink-0">
                  {davinciConnected ? 1 : 2}
                </span>
                <div>
                  <div className="font-semibold text-slate-200">Validar o Claude Code (MCP) no host</div>
                  <div className="text-[11px] text-slate-400 mt-0.5 leading-snug">
                    Esta tela não consulta o MCP. Confirme a conexão no computador host.
                  </div>
                </div>
              </div>

              <div className="flex gap-2.5">
                <span className="w-5 h-5 rounded-full bg-cyan-500/20 text-cyan-400 font-bold text-xs flex items-center justify-center shrink-0">
                  {davinciConnected ? 2 : 3}
                </span>
                <div>
                  <div className="font-semibold text-slate-200">Atualizar e verificar novamente</div>
                  <div className="text-[11px] text-slate-400 mt-0.5 leading-snug">
                    Clique em &apos;Atualizar verificações&apos; para reconsultar o host.
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Rodapé */}
      <div className="p-4 rounded-2xl bg-[#0a1222] border border-slate-800 flex flex-col md:flex-row items-center justify-between gap-4">
        <div className="flex items-center gap-3 text-xs text-slate-300">
          <Lightbulb className="w-5 h-5 text-amber-400 shrink-0" />
          <span>
            <strong className="text-white">Dica:</strong> você pode continuar sem o DaVinci para realizar etapas de análise,
            transcrição e planejamento narrativo, mas a montagem e exportação final exigem o DaVinci Resolve Studio instalado.
          </span>
        </div>

        <div className="flex items-center gap-3 shrink-0">
          <div className="flex flex-col items-end">
            <button
              type="button"
              disabled
              title={DOC_NOTE}
              className="px-4 py-2 rounded-xl bg-slate-800 text-xs font-semibold text-slate-400 transition-all flex items-center gap-1.5 cursor-not-allowed opacity-60"
            >
              Guia de instalação <ExternalLink className="w-3.5 h-3.5" />
            </button>
            <span className="text-[10px] text-slate-500 mt-1">{DOC_NOTE}</span>
          </div>
          <button
            onClick={onContinue}
            className="px-5 py-2 rounded-xl bg-gradient-to-r from-cyan-500 to-blue-600 hover:from-cyan-400 hover:to-blue-500 text-xs font-bold text-white transition-all shadow-md shadow-cyan-950 flex items-center gap-2"
          >
            Iniciar preparação do projeto <ArrowRight className="w-4 h-4" />
          </button>
        </div>
      </div>
    </div>
  );
};
