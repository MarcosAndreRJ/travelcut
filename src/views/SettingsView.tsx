import React, { useState } from 'react';
import {
  Settings,
  Cpu,
  Sparkles,
  HardDrive,
  Palette,
  Eye,
  Key,
  Folder,
  CheckCircle2,
  Save,
  RotateCcw,
  Sliders,
  ShieldCheck,
  Zap,
  Globe
} from 'lucide-react';

export const SettingsView: React.FC = () => {
  const [whisperModel, setWhisperModel] = useState<'large-v3' | 'medium' | 'base'>('large-v3');
  const [visionModel, setVisionModel] = useState<'yolov9-clip' | 'fast-mobilenet'>('yolov9-clip');
  const [gpuDevice, setGpuDevice] = useState<'cuda' | 'directml' | 'cpu'>('cuda');
  const [samplingRate, setSamplingRate] = useState<number>(2);
  const [selectedLut, setSelectedLut] = useState<string>('lut-tokyo');
  const [colorSpace, setColorSpace] = useState<'rec709' | 'dcip3' | 'aces'>('rec709');
  const [projectPath, setProjectPath] = useState<string>('D:\\TravelCut_Studio\\Projetos');
  const [savedSuccess, setSavedSuccess] = useState(false);
  const [cacheSize, setCacheSize] = useState('14.2 GB');

  const handleSave = () => {
    setSavedSuccess(true);
    setTimeout(() => setSavedSuccess(false), 3000);
  };

  const handleClearCache = () => {
    setCacheSize('0 MB');
  };

  return (
    <div className="flex-1 flex flex-col overflow-y-auto bg-studio-bg p-6 select-none">
      {/* Top Banner */}
      <div className="flex items-center justify-between pb-6 border-b border-studio-border">
        <div className="flex items-center space-x-3">
          <div className="p-2.5 bg-indigo-600/20 border border-indigo-500/40 rounded-xl">
            <Settings className="w-6 h-6 text-indigo-400" />
          </div>
          <div>
            <h2 className="text-base font-bold text-studio-text uppercase tracking-wide">
              Preferências & Motores de Inteligência Artificial
            </h2>
            <p className="text-xs text-studio-subtle">
              Configure modelos locais, aceleração por GPU, LUTs de cor e cache de pré-visualização
            </p>
          </div>
        </div>

        <button
          onClick={handleSave}
          className="flex items-center space-x-2 bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 text-white text-xs px-4 py-2 rounded-xl font-bold shadow-glow-indigo transition-all"
        >
          <Save className="w-3.5 h-3.5" />
          <span>Salvar Configurações</span>
        </button>
      </div>

      {savedSuccess && (
        <div className="mt-4 bg-emerald-500/20 border border-emerald-500/50 text-emerald-300 px-4 py-2.5 rounded-xl text-xs font-semibold flex items-center space-x-2">
          <CheckCircle2 className="w-4 h-4 text-emerald-400" />
          <span>Configurações salvas com sucesso no arquivo do projeto!</span>
        </div>
      )}

      {/* Grid of Setting Sections */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mt-6">
        {/* Section 1: Motores de IA & Transcrição */}
        <div className="bg-studio-panel border border-studio-border rounded-2xl p-5 space-y-4">
          <div className="flex items-center space-x-2 pb-2 border-b border-studio-border">
            <Cpu className="w-4 h-4 text-indigo-400" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-studio-text">
              Transcrição de Voz (Whisper IA)
            </h3>
          </div>

          <div className="space-y-3 text-xs">
            <div>
              <label className="text-studio-muted font-semibold block mb-1.5">
                Modelo Speech-to-Text Local
              </label>
              <div className="grid grid-cols-3 gap-2">
                {[
                  { id: 'large-v3', label: 'Whisper v3 Large', desc: 'Máxima precisão (99.4%)' },
                  { id: 'medium', label: 'Whisper Medium', desc: 'Equilíbrio velocidade/precisão' },
                  { id: 'base', label: 'Whisper Base', desc: 'Tempo real leve' },
                ].map((m) => (
                  <button
                    key={m.id}
                    onClick={() => setWhisperModel(m.id as any)}
                    className={`p-2.5 rounded-xl border text-left transition-all ${
                      whisperModel === m.id
                        ? 'bg-indigo-950/40 border-indigo-500 text-white'
                        : 'bg-studio-card border-studio-border text-studio-muted hover:text-white'
                    }`}
                  >
                    <span className="font-bold block text-xs">{m.label}</span>
                    <span className="text-[10px] text-studio-subtle block mt-0.5">{m.desc}</span>
                  </button>
                ))}
              </div>
            </div>

            <div className="pt-2">
              <label className="text-studio-muted font-semibold block mb-1.5">
                Hardware de Aceleração de Rede Neural
              </label>
              <div className="grid grid-cols-3 gap-2">
                {[
                  { id: 'cuda', label: 'NVIDIA CUDA 12.4', sub: 'RTX 4090 24GB VRAM (Ativo)' },
                  { id: 'directml', label: 'DirectML / Vulkan', sub: 'Acelerador genérico' },
                  { id: 'cpu', label: 'CPU x86-64', sub: 'Modo sem aceleração' },
                ].map((g) => (
                  <button
                    key={g.id}
                    onClick={() => setGpuDevice(g.id as any)}
                    className={`p-2.5 rounded-xl border text-left transition-all ${
                      gpuDevice === g.id
                        ? 'bg-emerald-950/30 border-emerald-500 text-white'
                        : 'bg-studio-card border-studio-border text-studio-muted hover:text-white'
                    }`}
                  >
                    <span className="font-bold block text-xs">{g.label}</span>
                    <span className="text-[10px] text-studio-subtle block mt-0.5">{g.sub}</span>
                  </button>
                ))}
              </div>
            </div>
          </div>
        </div>

        {/* Section 2: Visão Computacional & Cortes */}
        <div className="bg-studio-panel border border-studio-border rounded-2xl p-5 space-y-4">
          <div className="flex items-center space-x-2 pb-2 border-b border-studio-border">
            <Eye className="w-4 h-4 text-indigo-400" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-studio-text">
              Visão Computacional & Análise de Vídeo
            </h3>
          </div>

          <div className="space-y-3.5 text-xs">
            <div>
              <label className="text-studio-muted font-semibold block mb-1.5">
                Detector de Cenas & Reconhecimento Semântico
              </label>
              <select
                value={visionModel}
                onChange={(e) => setVisionModel(e.target.value as any)}
                className="w-full bg-studio-card border border-studio-border rounded-lg px-3 py-2 text-xs text-studio-text focus:outline-none focus:border-indigo-500"
              >
                <option value="yolov9-clip">YOLOv9 + OpenAI CLIP ViT-L/14 (Recomendado para Viagens)</option>
                <option value="fast-mobilenet">MobileNet-V4 Fast Vision (Modo Leve)</option>
              </select>
            </div>

            <div>
              <div className="flex items-center justify-between mb-1.5">
                <label className="text-studio-muted font-semibold">
                  Taxa de Amostragem de Quadros (Sampling)
                </label>
                <span className="font-mono text-indigo-400 font-bold">{samplingRate} fps</span>
              </div>
              <input
                type="range"
                min="0.5"
                max="5"
                step="0.5"
                value={samplingRate}
                onChange={(e) => setSamplingRate(parseFloat(e.target.value))}
                className="w-full accent-indigo-500 h-1.5 bg-studio-card rounded cursor-pointer"
              />
              <span className="text-[11px] text-studio-subtle mt-1 block">
                Valores maiores analisam mais detalhes em detrimento de mais tempo de processamento.
              </span>
            </div>
          </div>
        </div>

        {/* Section 3: LUTs de Cor & Estilização Cinematográfica */}
        <div className="bg-studio-panel border border-studio-border rounded-2xl p-5 space-y-4">
          <div className="flex items-center space-x-2 pb-2 border-b border-studio-border">
            <Palette className="w-4 h-4 text-indigo-400" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-studio-text">
              Color Grading & LUTs de Viagem
            </h3>
          </div>

          <div className="space-y-3 text-xs">
            <div>
              <label className="text-studio-muted font-semibold block mb-1.5">
                LUT Cinematográfica Padrão da Viagem
              </label>
              <div className="grid grid-cols-2 gap-2">
                {[
                  { id: 'lut-tokyo', name: 'Tokyo Neon Cyberpunk', tone: 'Teal & Orange com realces luminosos' },
                  { id: 'lut-zen', name: 'Kyoto Zen Autumn', tone: 'Dourado caloroso e verdes suaves' },
                  { id: 'lut-tropical', name: 'Okinawa Island Blue', tone: 'Águas turquesas vibrantes' },
                  { id: 'lut-mist', name: 'Mount Fuji Moody Mist', tone: 'Contraste suave e névoa poética' },
                ].map((lut) => (
                  <button
                    key={lut.id}
                    onClick={() => setSelectedLut(lut.id)}
                    className={`p-2.5 rounded-xl border text-left transition-all ${
                      selectedLut === lut.id
                        ? 'bg-indigo-950/40 border-indigo-500 text-white'
                        : 'bg-studio-card border-studio-border text-studio-muted hover:text-white'
                    }`}
                  >
                    <span className="font-bold block text-xs">{lut.name}</span>
                    <span className="text-[10px] text-studio-subtle block mt-0.5">{lut.tone}</span>
                  </button>
                ))}
              </div>
            </div>

            <div className="pt-2">
              <label className="text-studio-muted font-semibold block mb-1.5">
                Espaço de Cor de Saída
              </label>
              <div className="flex space-x-2">
                {[
                  { id: 'rec709', label: 'Rec.709 (SDR Padrão Web/TV)' },
                  { id: 'dcip3', label: 'DCI-P3 (Displays Apple & Cinema)' },
                  { id: 'aces', label: 'ACEScc / HDR' },
                ].map((cs) => (
                  <button
                    key={cs.id}
                    onClick={() => setColorSpace(cs.id as any)}
                    className={`flex-1 py-1.5 px-2 rounded-lg border text-xs font-medium transition-all ${
                      colorSpace === cs.id
                        ? 'bg-indigo-600 border-indigo-500 text-white font-semibold'
                        : 'bg-studio-card border-studio-border text-studio-muted hover:text-white'
                    }`}
                  >
                    {cs.label}
                  </button>
                ))}
              </div>
            </div>
          </div>
        </div>

        {/* Section 4: Cache & Armazenamento */}
        <div className="bg-studio-panel border border-studio-border rounded-2xl p-5 space-y-4">
          <div className="flex items-center space-x-2 pb-2 border-b border-studio-border">
            <HardDrive className="w-4 h-4 text-indigo-400" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-studio-text">
              Armazenamento & Gerenciamento de Cache
            </h3>
          </div>

          <div className="space-y-3.5 text-xs">
            <div>
              <label className="text-studio-muted font-semibold block mb-1.5">
                Diretório Raiz de Projetos e Exportações
              </label>
              <div className="flex space-x-2">
                <input
                  type="text"
                  value={projectPath}
                  onChange={(e) => setProjectPath(e.target.value)}
                  className="flex-1 bg-studio-card border border-studio-border rounded-lg px-3 py-1.5 font-mono text-xs text-studio-text focus:outline-none focus:border-indigo-500"
                />
                <button
                  onClick={() => alert(`Caminho selecionado: ${projectPath}`)}
                  className="bg-studio-card hover:bg-studio-cardHover border border-studio-border px-3 py-1.5 rounded-lg text-studio-text"
                >
                  <Folder className="w-4 h-4 text-indigo-400" />
                </button>
              </div>
            </div>

            <div className="pt-2 flex items-center justify-between bg-studio-card border border-studio-border p-3 rounded-xl">
              <div>
                <span className="font-semibold text-studio-text block">Cache de Waveforms e Proxies</span>
                <span className="text-[11px] text-studio-subtle font-mono">{cacheSize} em disco SSD NVMe</span>
              </div>
              <button
                onClick={handleClearCache}
                className="bg-studio-panel hover:bg-rose-500/20 border border-studio-border hover:border-rose-500/40 text-studio-subtle hover:text-rose-400 px-3 py-1.5 rounded-lg font-semibold transition-colors"
              >
                Limpar Cache
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
