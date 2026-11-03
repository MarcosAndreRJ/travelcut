import React from 'react';
import {
  Plus,
  FolderOpen,
  Sparkles,
  Laptop,
  ArrowRight,
  ExternalLink,
  Film
} from 'lucide-react';
import { ProjectSummary } from '../types/ipc';

interface WelcomeStartViewProps {
  recentProjects?: ProjectSummary[];
  onCreateProject: () => void;
  onOpenProject: () => void;
  onImportExample?: () => void;
  onOpenRecent?: (project: ProjectSummary) => void;
}

export const WelcomeStartView: React.FC<WelcomeStartViewProps> = ({
  recentProjects = [],
  onCreateProject,
  onOpenProject,
  onImportExample,
  onOpenRecent
}) => {
  return (
    <div className="flex-1 overflow-y-auto bg-[#070d18] text-slate-100 p-8 space-y-8 font-sans">
      {/* Top Section: Hero with Mockup & Getting Started */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left Column (2 cols): Main Hero & Action Buttons */}
        <div className="lg:col-span-2 rounded-3xl bg-gradient-to-r from-[#0a1628] via-[#0d223f] to-[#0a1424] border border-cyan-500/30 p-8 flex flex-col justify-between shadow-2xl relative overflow-hidden">
          {/* Laptop mockup background element */}
          <div
            className="absolute right-0 bottom-0 top-0 w-2/5 opacity-30 bg-contain bg-no-repeat bg-right-bottom pointer-events-none"
            style={{ backgroundImage: `url('/assets/10.webp')` }}
          />

          <div className="relative z-10 max-w-xl">
            <div className="text-[11px] font-bold tracking-widest text-cyan-400 uppercase mb-2">
              BEM-VINDO AO TRAVELCUT STUDIO
            </div>
            <h1 className="text-3xl font-extrabold text-white tracking-tight mb-3">
              Crie seu próximo <span className="text-cyan-400">vídeo de viagem</span>
            </h1>
            <p className="text-sm text-slate-300 leading-relaxed mb-6">
              Organize suas mídias, analise o conteúdo com IA, edite de forma inteligente e
              produza vídeos incríveis das suas viagens diretamente na timeline do DaVinci Resolve.
            </p>

            <div className="flex flex-wrap items-center gap-3">
              <button
                onClick={onCreateProject}
                className="px-5 py-2.5 rounded-xl bg-gradient-to-r from-cyan-500 to-blue-600 hover:from-cyan-400 hover:to-blue-500 text-white font-bold text-xs transition-all shadow-md shadow-cyan-950 flex items-center gap-2"
              >
                <Plus className="w-4 h-4 stroke-[2.5]" />
                Novo projeto
              </button>

              <button
                onClick={onOpenProject}
                className="px-5 py-2.5 rounded-xl bg-slate-800/80 hover:bg-slate-700/80 border border-slate-700 text-slate-200 font-semibold text-xs transition-all flex items-center gap-2"
              >
                <FolderOpen className="w-4 h-4" />
                Abrir projeto
              </button>

              {onImportExample && (
                <button
                  onClick={onImportExample}
                  className="px-5 py-2.5 rounded-xl bg-slate-800/80 hover:bg-slate-700/80 border border-slate-700 text-slate-200 font-semibold text-xs transition-all flex items-center gap-2"
                >
                  <Sparkles className="w-4 h-4 text-cyan-400" />
                  Importar exemplo
                </button>
              )}
            </div>
          </div>

          {/* Visual Laptop Mockup Canvas */}
          <div className="relative z-10 mt-8 pt-6 border-t border-slate-700/50 flex items-center gap-4 text-xs text-slate-400">
            <Laptop className="w-5 h-5 text-cyan-400 shrink-0" />
            <span>
              Suporte nativo a formatos 4K 60fps / 10-bit LOG de drones, câmeras Sony, GoPro e iPhones.
            </span>
          </div>
        </div>

        {/* Right Column (1 col): Primeiros passos */}
        <div className="rounded-3xl bg-[#0a1222] border border-slate-800 p-6 flex flex-col justify-between">
          <div>
            <div className="flex items-center justify-between mb-2">
              <h3 className="text-base font-bold text-white">Primeiros passos</h3>
              <span className="text-[11px] font-semibold text-cyan-400">Guia rápido</span>
            </div>
            <p className="text-xs text-slate-400 mb-5">
              Siga o fluxo recomendado para criar seu primeiro vídeo de viagem.
            </p>

            <div className="space-y-3.5">
              {[
                { step: '1', title: 'Crie um projeto', desc: 'Defina a pasta canônica com as mídias da viagem.' },
                { step: '2', title: 'Adicione suas mídias', desc: 'Separe vídeos brutos, fotos e trilhas sonoras.' },
                { step: '3', title: 'Analise o conteúdo', desc: 'IA transcreve falas e identifica momentos de destaque.' },
                { step: '4', title: 'Edite e revise', desc: 'Valide as sugestões de corte e monte a narrativa.' },
                { step: '5', title: 'Exporte e compartilhe', desc: 'Sincronize com a timeline do DaVinci e renderize.' }
              ].map((item) => (
                <div key={item.step} className="flex items-start gap-3">
                  <div className="w-6 h-6 rounded-lg bg-cyan-950/80 border border-cyan-500/40 text-cyan-300 font-bold text-xs flex items-center justify-center shrink-0 mt-0.5">
                    {item.step}
                  </div>
                  <div>
                    <h4 className="text-xs font-bold text-slate-200">{item.title}</h4>
                    <p className="text-[11px] text-slate-400">{item.desc}</p>
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Documentação entregue no pacote de teste; não há link externo nesta versão. */}
          <div className="mt-6 flex flex-col gap-1">
            <button
              type="button"
              disabled
              title="Documentação no pacote: LEIA-ME-HOST.md"
              className="text-xs text-slate-500 font-semibold flex items-center gap-1.5 cursor-not-allowed"
            >
              Ver documentação completa <ExternalLink className="w-3.5 h-3.5" />
            </button>
            <span className="text-[10px] text-slate-500">Documentação no pacote: LEIA-ME-HOST.md</span>
          </div>
        </div>
      </div>

      {/* Bottom Section: Projetos recentes */}
      <div className="rounded-3xl bg-[#0a1222] border border-slate-800 p-6">
        <div className="flex items-center justify-between mb-4">
          <div>
            <h3 className="text-base font-bold text-white">Projetos recentes</h3>
            <p className="text-xs text-slate-400">Acesse rapidamente seus projetos em andamento.</p>
          </div>
          <button
            onClick={onOpenProject}
            className="text-xs font-semibold text-cyan-400 hover:text-cyan-300 flex items-center gap-1"
          >
            Ver todos <ArrowRight className="w-3.5 h-3.5" />
          </button>
        </div>

        {recentProjects.length > 0 ? (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            {recentProjects.map((p) => (
              <div
                key={p.projectId}
                onClick={() => onOpenRecent?.(p)}
                className="p-4 rounded-2xl bg-[#080d19] border border-slate-800 hover:border-cyan-500/50 cursor-pointer transition-all"
              >
                <div className="flex items-center gap-2 mb-2">
                  <Film className="w-4 h-4 text-cyan-400" />
                  <span className="text-xs font-bold text-white truncate">{p.name}</span>
                </div>
                <div className="text-[11px] text-slate-400 truncate mb-3">{p.rootPath}</div>
                <div className="flex items-center justify-between text-[10px] text-slate-500">
                  <span>{p.mediaCount} clipes</span>
                  <span className="text-emerald-400">{p.pendingReviewsCount ?? 0} para revisar</span>
                </div>
              </div>
            ))}
          </div>
        ) : (
          <div className="p-12 rounded-2xl bg-[#080d19]/60 border border-slate-800/80 flex flex-col items-center justify-center text-center">
            <div className="w-16 h-16 rounded-2xl bg-slate-900 border border-slate-800 flex items-center justify-center text-slate-500 mb-3">
              <FolderOpen className="w-8 h-8" />
            </div>
            <h4 className="text-sm font-bold text-slate-200 mb-1">Nenhum projeto recente</h4>
            <p className="text-xs text-slate-400 max-w-sm mb-4">
              Crie um novo projeto, abra um existente ou importe um exemplo para começar.
            </p>
            <button
              onClick={onCreateProject}
              className="px-4 py-2 rounded-xl bg-cyan-600/20 hover:bg-cyan-600/30 border border-cyan-500/40 text-cyan-300 font-semibold text-xs transition-all flex items-center gap-1.5"
            >
              <Plus className="w-3.5 h-3.5" /> Criar projeto agora
            </button>
          </div>
        )}
      </div>
    </div>
  );
};
