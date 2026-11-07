import React, { useState } from 'react';
import {
  Download,
  HardDrive,
  Cpu,
  Wifi,
  X,
  FileAudio
} from 'lucide-react';

interface ModelDownloadModalProps {
  isOpen: boolean;
  onConfirm: (modelName: string) => void;
  onClose: () => void;
}

export const ModelDownloadModal: React.FC<ModelDownloadModalProps> = ({
  isOpen,
  onConfirm,
  onClose
}) => {
  const [modelName] = useState('large-v3');
  const [hasConfirmedAwareness, setHasConfirmedAwareness] = useState(false);

  if (!isOpen) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="model-download-title"
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm"
    >
      <div className="bg-[#0b1326] border border-slate-700/80 rounded-2xl max-w-lg w-full shadow-2xl overflow-hidden animate-in fade-in zoom-in-95 duration-150">
        {/* Header */}
        <div className="flex items-center justify-between p-6 border-b border-slate-800 bg-[#070d18]">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl bg-cyan-500/10 border border-cyan-500/30 flex items-center justify-center text-cyan-400">
              <FileAudio className="w-5 h-5" />
            </div>
            <div>
              <h3 id="model-download-title" className="text-base font-bold text-white">
                Download do Modelo Whisper
              </h3>
              <p className="text-xs text-slate-400">
                Confirmação explícita de download para o host Windows
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="p-2 text-slate-400 hover:text-white rounded-lg hover:bg-slate-800 transition"
            aria-label="Fechar modal"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 space-y-5 text-sm text-slate-300">
          <div className="p-3.5 rounded-xl bg-cyan-950/30 border border-cyan-500/30 text-cyan-200 text-xs leading-relaxed">
            O TravelCut Studio utiliza o modelo <strong>Whisper large-v3</strong> para transcrição com alta precisão, pontuação exata e detecção de silêncios em viagens.
          </div>

          {/* Model info specs */}
          <div className="grid grid-cols-2 gap-3 text-xs">
            <div className="p-3 rounded-xl bg-[#090f1d] border border-slate-800 space-y-1">
              <div className="flex items-center gap-1.5 text-slate-400">
                <HardDrive className="w-3.5 h-3.5 text-cyan-400" />
                <span>Tamanho em disco</span>
              </div>
              <div className="text-sm font-bold text-white">~2,9 GB</div>
              <div className="text-[11px] text-slate-500">Checkpoint PyTorch (.pt)</div>
            </div>

            <div className="p-3 rounded-xl bg-[#090f1d] border border-slate-800 space-y-1">
              <div className="flex items-center gap-1.5 text-slate-400">
                <Cpu className="w-3.5 h-3.5 text-cyan-400" />
                <span>Execução</span>
              </div>
              <div className="text-sm font-bold text-white">GPU CUDA ou CPU</div>
              <div className="text-[11px] text-slate-500">Na CPU a transcrição é bem mais lenta</div>
            </div>
          </div>

          {/* Network Notice */}
          <div className="p-3 rounded-xl bg-amber-950/30 border border-amber-500/30 text-amber-200 text-xs flex items-start gap-2.5">
            <Wifi className="w-4 h-4 text-amber-400 shrink-0 mt-0.5" />
            <div>
              <span className="font-bold">Aviso de consumo de rede:</span>
              <p className="text-amber-200/80 mt-0.5">
                O arquivo será baixado da URL oficial do OpenAI Whisper (openaipublic.azureedge.net) para a pasta de cache do Whisper neste computador e validado por SHA-256.
              </p>
            </div>
          </div>

          {/* Awareness Checkbox */}
          <label className="flex items-start gap-2.5 p-3 rounded-xl bg-[#090f1d] border border-slate-800/80 cursor-pointer text-xs">
            <input
              type="checkbox"
              checked={hasConfirmedAwareness}
              onChange={(e) => setHasConfirmedAwareness(e.target.checked)}
              className="w-4 h-4 mt-0.5 rounded border-slate-700 text-cyan-500 focus:ring-cyan-500/30 shrink-0"
            />
            <span className="text-slate-300">
              Estou ciente do download de ~2,9 GB e confirmo o início da transferência para a pasta de cache do Whisper.
            </span>
          </label>
        </div>

        {/* Footer actions */}
        <div className="p-4 border-t border-slate-800 bg-[#070d18] flex items-center justify-end gap-3">
          <button
            onClick={onClose}
            className="px-4 py-2 text-xs font-semibold rounded-xl text-slate-300 hover:text-white hover:bg-slate-800 transition"
          >
            Cancelar
          </button>
          <button
            disabled={!hasConfirmedAwareness}
            onClick={() => onConfirm(modelName)}
            className="px-5 py-2 text-xs font-bold rounded-xl bg-cyan-600 hover:bg-cyan-500 text-white transition disabled:opacity-40 disabled:cursor-not-allowed flex items-center gap-2 shadow-lg shadow-cyan-600/20"
          >
            <Download className="w-4 h-4" />
            Iniciar Download do large-v3
          </button>
        </div>
      </div>
    </div>
  );
};
