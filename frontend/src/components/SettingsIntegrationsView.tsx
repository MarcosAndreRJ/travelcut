import React, { useState } from 'react';
import {
  Settings,
  Cpu,
  Sliders,
  ShieldCheck,
  RefreshCw,
  Server
} from 'lucide-react';
import { EditorCapabilities } from '../types/ipc';
import { ipc } from '../services/ipcBridge';
import { EditorBadge } from './editor/EditorPrimitives';
import {
  buildIntegrationRows,
  type IntegrationId,
  type IntegrationRow
} from './environment/integrationStatus';
import { useIntegrationStatus } from './environment/useIntegrationStatus';

interface SettingsIntegrationsViewProps {
  editorCaps: EditorCapabilities | null;
  /** Reconsulta o estado do DaVinci no host (editor.capabilities), feita pela App. */
  onRefreshCaps?: () => void | Promise<void>;
}

type SubTab = 'integrations' | 'editorial' | 'safety';

interface EngineCardSpec {
  rowId: IntegrationId;
  category: string;
  /** Linhas extras mostradas dentro do cartão (ex.: Python e GPU do Whisper). */
  extraIds: IntegrationId[];
  testSource: 'editor' | 'host';
}

const ENGINE_CARDS: EngineCardSpec[] = [
  { rowId: 'davinci', category: 'NLE / Editor API', extraIds: [], testSource: 'editor' },
  { rowId: 'whisper', category: 'Motor de transcrição', extraIds: ['python', 'cuda'], testSource: 'host' },
  { rowId: 'ffmpeg', category: 'Processamento multimídia', extraIds: [], testSource: 'host' },
  { rowId: 'hyperframes', category: 'Animações (opcional)', extraIds: [], testSource: 'host' }
];

const PLANNED_BADGE = { label: 'Planejado — não implementado', tone: 'amber' } as const;

const EDITORIAL_PLANNED: { title: string; description: string }[] = [
  {
    title: 'Duração mínima de silêncio',
    description: 'Pausas sem fala acima deste tempo serão candidatas a corte.'
  },
  {
    title: 'Margem de fala (head & tail)',
    description: 'Tempo mantido antes e depois de cada palavra cortada para evitar corte seco.'
  },
  {
    title: 'Subtle zoom em jump cuts',
    description: 'Escala sutil aplicada entre cortes consecutivos na mesma tomada.'
  },
  {
    title: 'Pré-aprovação por confiança',
    description: 'Limiar em escala qualitativa (baixa, média, alta). Não há percentual de confiança nesta versão.'
  }
];

const DemoTag: React.FC = () => (
  <span className="px-2 py-0.5 rounded-full text-[10px] font-bold uppercase tracking-wider bg-amber-500/10 text-amber-300 border border-amber-500/30">
    DEMONSTRAÇÃO
  </span>
);

export const SettingsIntegrationsView: React.FC<SettingsIntegrationsViewProps> = ({
  editorCaps,
  onRefreshCaps
}) => {
  const [activeSubTab, setActiveSubTab] = useState<SubTab>('integrations');
  const [testing, setTesting] = useState<string | null>(null);
  const status = useIntegrationStatus();
  const isMock = ipc.isMock;

  const rows = buildIntegrationRows({
    isMock,
    analysisEnv: status.analysisEnv,
    analysisError: status.analysisError,
    editorCaps,
    motionCaps: status.motionCaps,
    motionError: status.motionError
  });
  const rowById = (id: IntegrationId): IntegrationRow | undefined => rows.find((r) => r.id === id);

  /** "Testar conexão" só reconsulta a capacidade real; nenhum resultado é fabricado. */
  const handleTest = async (spec: EngineCardSpec) => {
    setTesting(spec.rowId);
    try {
      if (spec.testSource === 'editor') {
        await Promise.resolve(onRefreshCaps?.());
      } else {
        await status.refresh();
      }
    } catch (err) {
      console.error('Falha ao reconsultar integração:', err);
    } finally {
      setTesting(null);
    }
  };

  const subTabClass = (tab: SubTab): string =>
    `pb-3 px-3 text-xs font-bold border-b-2 transition-all flex items-center gap-2 ${
      activeSubTab === tab ? 'border-cyan-400 text-cyan-300' : 'border-transparent text-slate-400 hover:text-slate-200'
    }`;

  return (
    <div className="flex-1 flex flex-col h-full bg-[#070d18] text-slate-100 font-sans overflow-hidden">
      {/* Cabeçalho */}
      <div className="px-6 py-3 border-b border-slate-800 bg-[#0a1222] flex items-center justify-between shrink-0">
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2">
            <Settings className="w-5 h-5 text-cyan-400" />
            <h2 className="text-sm font-bold text-white">Configurações, Motores & Perfil Editorial</h2>
          </div>
          {isMock && <DemoTag />}
        </div>
        <span className="text-xs text-slate-400">
          Nesta versão as configurações não são salvas nem aplicadas ao pipeline.
        </span>
      </div>

      {/* Abas */}
      <div className="px-6 pt-3 border-b border-slate-800 bg-[#080e1b] flex items-center gap-2 shrink-0">
        <button onClick={() => setActiveSubTab('integrations')} className={subTabClass('integrations')}>
          <Server className="w-4 h-4" /> Conectividade & Motores
        </button>
        <button onClick={() => setActiveSubTab('editorial')} className={subTabClass('editorial')}>
          <Sliders className="w-4 h-4" /> Perfil Editorial & Corte
        </button>
        <button onClick={() => setActiveSubTab('safety')} className={subTabClass('safety')}>
          <ShieldCheck className="w-4 h-4" /> Preservação & Auditoria
        </button>
      </div>

      <div className="flex-1 overflow-y-auto p-6 space-y-6 max-w-6xl w-full mx-auto">
        {/* Aba 1: motores (estado real do host) */}
        {activeSubTab === 'integrations' && (
          <div className="space-y-4">
            <div>
              <h3 className="text-sm font-bold text-white flex items-center gap-2">
                <Cpu className="w-4 h-4 text-cyan-400" />
                Motores de execução local e integrações
              </h3>
              <p className="text-xs text-slate-400 mt-1">
                Estados lidos do host. &quot;Não verificado&quot; significa que esta tela não obteve leitura real.
              </p>
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {ENGINE_CARDS.map((spec) => {
                const row = rowById(spec.rowId);
                if (!row) return null;
                const isTesting = testing === spec.rowId || (spec.testSource === 'host' && status.loading);
                return (
                  <div
                    key={spec.rowId}
                    className="p-5 rounded-2xl bg-[#0a1424] border border-slate-800 hover:border-slate-700 transition-all space-y-3"
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <span className="text-[10px] font-bold uppercase tracking-wider text-cyan-400">
                          {spec.category}
                        </span>
                        <h4 className="text-sm font-bold text-white mt-0.5">{row.name}</h4>
                        {row.version && (
                          <span className="text-xs text-slate-400 font-mono">{row.version}</span>
                        )}
                      </div>
                      <EditorBadge badge={{ label: row.label, tone: row.tone }} />
                    </div>

                    <div className="p-3 rounded-xl bg-[#070d18] border border-slate-800/80 text-xs space-y-2">
                      <p className="text-[11px] text-slate-300 leading-snug">{row.detail}</p>
                      <div className="flex justify-between text-[10px] text-slate-500 font-mono">
                        <span>Fonte</span>
                        <span className="truncate max-w-[220px]" title={row.source}>{row.source}</span>
                      </div>
                      {spec.extraIds.map((extraId) => {
                        const extra = rowById(extraId);
                        if (!extra) return null;
                        return (
                          <div key={extraId} className="flex items-center justify-between gap-2 pt-1 border-t border-slate-800/60">
                            <span className="text-slate-400">{extra.name}</span>
                            <EditorBadge badge={{ label: extra.label, tone: extra.tone }} />
                          </div>
                        );
                      })}
                    </div>

                    <div className="pt-1 flex justify-end">
                      <button
                        onClick={() => handleTest(spec)}
                        disabled={isTesting}
                        className="px-3 py-1.5 rounded-xl bg-[#0d1d36] hover:bg-[#12284c] border border-cyan-500/30 text-cyan-300 text-xs font-semibold flex items-center gap-1.5 transition-all disabled:opacity-60"
                      >
                        <RefreshCw className={`w-3.5 h-3.5 ${isTesting ? 'animate-spin' : ''}`} />
                        <span>{isTesting ? 'Consultando…' : 'Testar conexão'}</span>
                      </button>
                    </div>
                  </div>
                );
              })}
            </div>

            <p className="text-[11px] text-slate-500">
              Claude Code (MCP): esta tela não consulta o MCP. Valide a conexão no computador host.
            </p>
          </div>
        )}

        {/* Aba 2: perfil editorial (planejado, sem valores aplicados) */}
        {activeSubTab === 'editorial' && (
          <div className="space-y-6">
            <div>
              <h3 className="text-sm font-bold text-white flex items-center gap-2">
                <Sliders className="w-4 h-4 text-cyan-400" />
                Parâmetros editoriais e heurísticas de montagem
              </h3>
              <p className="text-xs text-slate-400 mt-1">
                Itens planejados. Nenhum valor é salvo, lido pelo pipeline ou aplicado nesta versão.
              </p>
            </div>

            <div className="p-4 rounded-xl bg-amber-950/30 border border-amber-500/30 text-amber-200 text-xs leading-relaxed">
              Controles de edição indisponíveis: a configuração ainda não está implementada.
            </div>

            <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
              {EDITORIAL_PLANNED.map((item) => (
                <div key={item.title} className="p-5 rounded-2xl bg-[#0a1424] border border-slate-800 space-y-3">
                  <div className="flex items-center justify-between gap-3">
                    <span className="text-xs font-bold text-white">{item.title}</span>
                    <EditorBadge badge={PLANNED_BADGE} />
                  </div>
                  <p className="text-xs text-slate-400 leading-snug">{item.description}</p>
                </div>
              ))}
            </div>
          </div>
        )}

        {/* Aba 3: preservação e auditoria */}
        {activeSubTab === 'safety' && (
          <div className="space-y-6">
            <div>
              <h3 className="text-sm font-bold text-white flex items-center gap-2">
                <ShieldCheck className="w-4 h-4 text-emerald-400" />
                Regras de preservação dos arquivos
              </h3>
              <p className="text-xs text-slate-400 mt-1">
                Regras de projeto. A verificação automática dessas regras não é exibida nesta versão.
              </p>
            </div>

            <div className="space-y-4">
              <div className="p-5 rounded-2xl bg-[#0a1424] border border-emerald-500/30 flex items-start gap-4">
                <div className="w-10 h-10 rounded-xl bg-emerald-500/20 text-emerald-400 flex items-center justify-center shrink-0">
                  <ShieldCheck className="w-6 h-6" />
                </div>
                <div className="space-y-1">
                  <h4 className="text-sm font-bold text-white">1-Bruto e 2-Assets em modo somente leitura</h4>
                  <p className="text-xs text-slate-300 leading-relaxed">
                    As mídias originais ficam em 1-Bruto e os recursos de apoio em 2-Assets. Temporários e análises
                    ficam em 3-Trabalho; somente materiais aprovados vão para 4-Final.
                  </p>
                </div>
              </div>

              <div className="p-5 rounded-2xl bg-[#0a1424] border border-slate-800 space-y-3">
                <div className="flex items-center justify-between gap-3">
                  <h4 className="text-sm font-bold text-white">Trilha de auditoria</h4>
                  <EditorBadge badge={{ label: 'Planejada — não implementada', tone: 'amber' }} />
                </div>
                <p className="text-xs text-slate-400">
                  Registro de decisões com carimbo de tempo UTC ainda não é exibido nem exportado nesta versão.
                </p>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};
