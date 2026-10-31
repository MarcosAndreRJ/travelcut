import React, { useState } from 'react';
import { Folder, FolderOpen, Film, Sparkles, X, AlertCircle } from 'lucide-react';
import { selectFolderDialog } from '../../services/desktopBridge';

interface NewProjectModalProps {
  isOpen: boolean;
  onClose: () => void;
  onCreateProject: (projectName: string, folderPath: string) => Promise<void>;
}

export const NewProjectModal: React.FC<NewProjectModalProps> = ({
  isOpen,
  onClose,
  onCreateProject,
}) => {
  const [projectName, setProjectName] = useState('');
  const [folderPath, setFolderPath] = useState('');
  const [isSelectingFolder, setIsSelectingFolder] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!isOpen) return null;

  const handleSelectFolder = async () => {
    try {
      setIsSelectingFolder(true);
      setError(null);
      const selected = await selectFolderDialog('Selecione a pasta com os vídeos da viagem');
      if (selected) {
        setFolderPath(selected);
        if (!projectName.trim()) {
          // Suggest project name based on folder name
          const folderName = selected.split(/[/\\]/).filter(Boolean).pop() || '';
          if (folderName) {
            setProjectName(folderName.replace(/[_-]/g, ' '));
          }
        }
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(`Erro ao selecionar diretório: ${msg}`);
    } finally {
      setIsSelectingFolder(false);
    }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!projectName.trim()) {
      setError('Por favor, informe um nome para o projeto.');
      return;
    }
    if (!folderPath.trim()) {
      setError('Por favor, selecione a pasta de mídia dos vídeos da viagem.');
      return;
    }

    try {
      setIsSubmitting(true);
      setError(null);
      await onCreateProject(projectName.trim(), folderPath.trim());
      onClose();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : String(err);
      setError(`Erro ao criar projeto: ${msg}`);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm animate-in fade-in duration-200">
      <div
        className="w-full max-w-lg bg-[#0c121e] border border-slate-700/80 rounded-2xl shadow-2xl overflow-hidden flex flex-col"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-800 bg-[#090d16]">
          <div className="flex items-center space-x-2.5">
            <div className="p-2 rounded-lg bg-blue-600/20 text-blue-400 border border-blue-500/30">
              <Film className="w-5 h-5" />
            </div>
            <div>
              <h2 className="text-base font-bold text-white tracking-wide">Novo Projeto de Viagem</h2>
              <p className="text-xs text-slate-400">Configure o projeto e selecione a pasta de vídeos reais</p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-1 text-slate-400 hover:text-white rounded-lg hover:bg-slate-800 transition-colors"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Form Body */}
        <form onSubmit={handleSubmit} className="p-6 space-y-5">
          {error && (
            <div className="flex items-start space-x-2.5 p-3 rounded-xl bg-red-950/40 border border-red-800/60 text-red-300 text-xs">
              <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
              <span>{error}</span>
            </div>
          )}

          {/* Project Name Field */}
          <div className="space-y-1.5">
            <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider">
              Nome do Projeto
            </label>
            <input
              type="text"
              value={projectName}
              onChange={(e) => setProjectName(e.target.value)}
              placeholder="Ex: Viagem Fernando de Noronha 2026"
              className="w-full px-3.5 py-2.5 bg-slate-900 border border-slate-700 rounded-xl text-slate-100 placeholder-slate-500 text-sm focus:outline-none focus:border-blue-500 focus:ring-1 focus:ring-blue-500 transition-colors"
              autoFocus
            />
          </div>

          {/* Folder Path Field */}
          <div className="space-y-1.5">
            <label className="block text-xs font-semibold text-slate-300 uppercase tracking-wider">
              Pasta de Mídias (Vídeos Originais)
            </label>
            <div className="flex items-center space-x-2">
              <div className="relative flex-1">
                <input
                  type="text"
                  value={folderPath}
                  onChange={(e) => setFolderPath(e.target.value)}
                  placeholder="Selecione o diretório local..."
                  className="w-full pl-9 pr-3.5 py-2.5 bg-slate-900 border border-slate-700 rounded-xl text-slate-200 placeholder-slate-500 text-xs font-mono focus:outline-none focus:border-blue-500 transition-colors truncate"
                />
                <Folder className="w-4 h-4 text-slate-400 absolute left-3 top-3 pointer-events-none" />
              </div>
              <button
                type="button"
                onClick={handleSelectFolder}
                disabled={isSelectingFolder}
                className="px-4 py-2.5 bg-slate-800 hover:bg-slate-700 border border-slate-600 rounded-xl text-xs font-medium text-slate-200 flex items-center space-x-1.5 transition-colors shrink-0 disabled:opacity-50"
              >
                <FolderOpen className="w-4 h-4 text-blue-400" />
                <span>{isSelectingFolder ? 'Abrindo...' : 'Procurar'}</span>
              </button>
            </div>
            <p className="text-[11px] text-slate-400">
              O sistema escaneará recursivamente os arquivos de vídeo (.mp4, .mov, .mkv) e extrairá metadados técnicos reais.
            </p>
          </div>

          {/* Action Buttons */}
          <div className="pt-3 border-t border-slate-800 flex items-center justify-end space-x-3">
            <button
              type="button"
              onClick={onClose}
              disabled={isSubmitting}
              className="px-4 py-2 text-xs font-medium text-slate-400 hover:text-white rounded-xl transition-colors disabled:opacity-50"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={isSubmitting}
              className="px-5 py-2.5 bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-500 hover:to-indigo-500 text-white rounded-xl text-xs font-semibold shadow-lg shadow-blue-600/30 flex items-center space-x-2 transition-all disabled:opacity-50"
            >
              <Sparkles className="w-4 h-4" />
              <span>{isSubmitting ? 'Inicializando...' : 'Criar e Escanear Projeto'}</span>
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
