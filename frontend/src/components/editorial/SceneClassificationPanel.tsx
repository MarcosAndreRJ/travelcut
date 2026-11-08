import React, { useState } from 'react';
import { Layers, Check, Trash2 } from 'lucide-react';
import { ipc } from '../../services/ipcBridge';

export interface SceneClassificationPanelProps {
  projectId: string;
  mediaId: string;
  mediaDurationSeconds: number;
  onSaved?: () => void;
}

export const SceneClassificationPanel: React.FC<SceneClassificationPanelProps> = ({
  projectId,
  mediaId,
  mediaDurationSeconds,
  onSaved
}) => {
  const [isFullVideo, setIsFullVideo] = useState(true);
  const [startSec, setStartSec] = useState(0);
  const [endSec, setEndSec] = useState(Math.max(1, mediaDurationSeconds));
  const [environment, setEnvironment] = useState('unclassified');
  const [interaction, setInteraction] = useState('direct_to_camera');
  const [framing, setFraming] = useState('medium');
  const [movement, setMovement] = useState('static');
  const [profile, setProfile] = useState('natural');
  const [notes, setNotes] = useState('');
  const [saving, setSaving] = useState(false);
  const [statusMsg, setStatusMsg] = useState<string | null>(null);

  const handleSave = async () => {
    setSaving(true);
    setStatusMsg(null);
    try {
      await ipc.send('scene.upsert', {
        projectId,
        mediaId,
        isFullVideo,
        startSeconds: isFullVideo ? null : startSec,
        endSeconds: isFullVideo ? null : endSec,
        environment,
        interaction,
        framing,
        movement,
        editorialProfile: profile,
        notes: notes.trim() || null
      });
      setStatusMsg('Classificação salva com sucesso.');
      onSaved?.();
    } catch (e: any) {
      setStatusMsg(`Erro: ${e.message || 'Falha ao salvar'}`);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="p-4 bg-slate-900 border border-slate-800 rounded-xl space-y-3 text-xs text-slate-200">
      <div className="flex items-center justify-between font-bold text-slate-100 border-b border-slate-800 pb-2">
        <span className="flex items-center gap-1.5"><Layers className="w-4 h-4 text-cyan-400" /> Classificação Manual de Cena</span>
      </div>

      <div className="flex items-center gap-4">
        <label className="flex items-center gap-1.5 cursor-pointer">
          <input type="radio" checked={isFullVideo} onChange={() => setIsFullVideo(true)} />
          Vídeo Completo
        </label>
        <label className="flex items-center gap-1.5 cursor-pointer">
          <input type="radio" checked={!isFullVideo} onChange={() => setIsFullVideo(false)} />
          Intervalo Específico
        </label>
      </div>

      {!isFullVideo && (
        <div className="grid grid-cols-2 gap-2 bg-slate-950 p-2.5 rounded-lg border border-slate-800">
          <div>
            <label className="text-[10px] text-slate-400 block">Início (s)</label>
            <input
              type="number"
              step="0.1"
              min="0"
              value={startSec}
              onChange={(e) => setStartSec(parseFloat(e.target.value) || 0)}
              className="w-full bg-slate-900 border border-slate-700 rounded px-2 py-1 text-white"
            />
          </div>
          <div>
            <label className="text-[10px] text-slate-400 block">Fim (s)</label>
            <input
              type="number"
              step="0.1"
              min="0"
              value={endSec}
              onChange={(e) => setEndSec(parseFloat(e.target.value) || 0)}
              className="w-full bg-slate-900 border border-slate-700 rounded px-2 py-1 text-white"
            />
          </div>
        </div>
      )}

      <div className="grid grid-cols-2 gap-2.5">
        <div>
          <label className="text-[10px] text-slate-400 block mb-1">Ambiente</label>
          <select value={environment} onChange={(e) => setEnvironment(e.target.value)} className="w-full bg-slate-800 border border-slate-700 rounded p-1 text-white">
            <option value="unclassified">Não classificado</option>
            <option value="interior">Interior</option>
            <option value="exterior">Exterior</option>
            <option value="mixed">Misto</option>
          </select>
        </div>
        <div>
          <label className="text-[10px] text-slate-400 block mb-1">Interação</label>
          <select value={interaction} onChange={(e) => setInteraction(e.target.value)} className="w-full bg-slate-800 border border-slate-700 rounded p-1 text-white">
            <option value="direct_to_camera">Fala direta à câmera</option>
            <option value="dialogue">Diálogo entre pessoas</option>
            <option value="interview">Entrevista</option>
            <option value="narration">Narração</option>
            <option value="broll">B-roll</option>
            <option value="landscape">Paisagem / ambiente</option>
            <option value="other">Outro</option>
          </select>
        </div>
        <div>
          <label className="text-[10px] text-slate-400 block mb-1">Enquadramento</label>
          <select value={framing} onChange={(e) => setFraming(e.target.value)} className="w-full bg-slate-800 border border-slate-700 rounded p-1 text-white">
            <option value="unspecified">Não informado</option>
            <option value="wide">Aberto</option>
            <option value="medium">Médio</option>
            <option value="close_up">Fechado</option>
          </select>
        </div>
        <div>
          <label className="text-[10px] text-slate-400 block mb-1">Movimento</label>
          <select value={movement} onChange={(e) => setMovement(e.target.value)} className="w-full bg-slate-800 border border-slate-700 rounded p-1 text-white">
            <option value="unspecified">Não informado</option>
            <option value="static">Câmera estática</option>
            <option value="moving">Câmera em movimento</option>
          </select>
        </div>
      </div>

      <div>
        <label className="text-[10px] text-slate-400 block mb-1">Perfil Editorial</label>
        <select value={profile} onChange={(e) => setProfile(e.target.value)} className="w-full bg-slate-800 border border-slate-700 rounded p-1 text-white">
          <option value="natural">Natural</option>
          <option value="dynamic">Dinâmico</option>
          <option value="cinematic">Cinematográfico</option>
        </select>
      </div>

      <div className="flex items-center justify-between pt-2">
        <button
          type="button"
          disabled={saving}
          onClick={handleSave}
          className="flex items-center gap-1.5 px-3 py-1.5 bg-cyan-600 hover:bg-cyan-500 text-white rounded-lg font-medium transition-all"
        >
          <Check className="w-3.5 h-3.5" /> {saving ? 'Salvando...' : 'Salvar Classificação'}
        </button>
        {statusMsg && <span className="text-[11px] text-cyan-300 font-medium">{statusMsg}</span>}
      </div>
    </div>
  );
};
