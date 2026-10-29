import React, { useState } from 'react';
import { HomeDashboard } from './HomeDashboard';
import { initialHomeData } from './data/homeMockData';
import { HomeDashboardData } from './types';
import { Sparkles, Eye, RotateCcw } from 'lucide-react';

/**
 * HomeStandaloneTest:
 * Visualização independente para testes e validação da Fase 2 (Home Dashboard),
 * sem interferir no aplicativo existente (App.tsx permanece intocado conforme Regra 3).
 */
export const HomeStandaloneTest: React.FC = () => {
  const [data, setData] = useState<HomeDashboardData>(initialHomeData);
  const [activeTab, setActiveTab] = useState<string>('home');
  const [logMessages, setLogMessages] = useState<string[]>([
    'Ambiente de teste independente da Fase 2 carregado.',
  ]);

  const addLog = (msg: string) => {
    setLogMessages((prev) => [
      `[${new Date().toLocaleTimeString()}] ${msg}`,
      ...prev.slice(0, 9),
    ]);
  };

  const handleResetData = () => {
    setData(initialHomeData);
    addLog('Dados do mock reiniciados para o estado padrão.');
  };

  return (
    <div className="min-h-screen bg-[#080b11] text-slate-100 flex flex-col font-sans">
      {/* Test Harness Header */}
      <header className="w-full bg-[#0d121c] border-b border-slate-800/80 px-4 py-3 flex items-center justify-between z-20">
        <div className="flex items-center gap-3">
          <div className="w-7 h-7 rounded-lg bg-cyan-500/20 border border-cyan-500/30 flex items-center justify-center text-cyan-400">
            <Sparkles size={16} />
          </div>
          <div>
            <h1 className="text-sm font-bold text-white flex items-center gap-2">
              <span>TravelCut Studio</span>
              <span className="text-[10px] font-semibold uppercase tracking-wider px-2 py-0.5 rounded bg-cyan-500/10 text-cyan-400 border border-cyan-500/20">
                Fase 2: Home Dashboard
              </span>
            </h1>
            <p className="text-[11px] text-slate-400">
              Visualização de Teste Independente • Baseado em 01_Painel_Inicial.png
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={handleResetData}
            className="flex items-center gap-1.5 px-3 py-1.5 bg-slate-800 hover:bg-slate-700 text-slate-300 rounded-lg text-xs font-medium border border-slate-700 transition-colors"
          >
            <RotateCcw size={13} />
            <span>Reiniciar Mock</span>
          </button>

          <div className="flex items-center gap-1 bg-slate-900 border border-slate-800 rounded-lg p-1 text-xs">
            <span className="text-slate-400 px-2 py-0.5 flex items-center gap-1 font-medium">
              <Eye size={12} className="text-emerald-400" />
              <span>Aba ativa:</span>
            </span>
            <span className="text-cyan-300 font-semibold px-2 py-0.5 bg-cyan-500/10 rounded">
              {activeTab}
            </span>
          </div>
        </div>
      </header>

      {/* Main Content Area */}
      <main className="flex-1 overflow-y-auto">
        <HomeDashboard
          initialData={data}
          onNavigateToTab={(tab) => {
            setActiveTab(tab);
            addLog(`Navegação disparada para a aba: "${tab}"`);
          }}
          onOpenFolder={(path) => {
            addLog(`Ação: Abrir pasta no sistema operacional "${path}"`);
          }}
          onStartAnalysis={() => {
            addLog('Ação: Iniciar análise completa dos arquivos de mídia');
          }}
          onMediaSelect={(media) => {
            addLog(`Mídia selecionada: ${media.filename} (${media.categoryLabel})`);
          }}
        />
      </main>

      {/* Test Log Bar */}
      <footer className="w-full bg-[#0a0e17] border-t border-slate-800/80 px-4 py-2 text-xs flex items-center justify-between text-slate-400 font-mono">
        <div className="flex items-center gap-2 overflow-hidden truncate">
          <span className="text-emerald-400 font-bold shrink-0">Último Evento:</span>
          <span className="text-slate-300 truncate">
            {logMessages[0] || 'Nenhum evento registrado ainda.'}
          </span>
        </div>
        <div className="shrink-0 text-slate-500 pl-4">
          Status: Pronto para integração no AppShell da Fase 1
        </div>
      </footer>
    </div>
  );
};

export default HomeStandaloneTest;
