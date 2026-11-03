import React, { useEffect, useRef } from 'react';
import { Play } from 'lucide-react';

interface SplashScreenProps {
  onFinish?: () => void;
  autoDismiss?: boolean;
}

/** Tempo da tela de abertura. Não há progresso real: nenhum módulo é "carregado" aqui. */
const SPLASH_DURATION_MS = 1500;

/**
 * Tela de abertura somente com "Iniciando…". Não afirma estado de .NET, Whisper, FFmpeg ou DaVinci:
 * esses estados só existem depois da consulta ao host (ver Verificação do ambiente).
 */
export const SplashScreen: React.FC<SplashScreenProps> = ({ onFinish, autoDismiss = true }) => {
  // Ref: o pai passa uma arrow nova a cada render; o timer não deve reiniciar por isso.
  const finishRef = useRef(onFinish);
  finishRef.current = onFinish;

  useEffect(() => {
    if (!autoDismiss) return;
    const timer = setTimeout(() => finishRef.current?.(), SPLASH_DURATION_MS);
    return () => clearTimeout(timer);
  }, [autoDismiss]);

  return (
    <div className="relative w-screen h-screen overflow-hidden bg-[#070d18] text-white flex flex-col justify-between p-8 select-none font-sans">
      {/* Background */}
      <div className="absolute inset-0 pointer-events-none">
        <div className="absolute top-0 right-0 w-3/4 h-full bg-gradient-to-l from-cyan-900/20 via-[#0a1b33]/40 to-transparent" />
        <div className="absolute -top-40 right-10 w-[700px] h-[700px] rounded-full bg-cyan-500/10 blur-[130px]" />
        <div className="absolute bottom-0 left-0 w-[500px] h-[500px] rounded-full bg-blue-600/10 blur-[120px]" />

        <svg className="absolute inset-0 w-full h-full opacity-30" viewBox="0 0 1200 800" fill="none">
          <path
            d="M -100 450 C 300 200, 700 150, 1300 280"
            stroke="url(#splash-glow)"
            strokeWidth="3"
          />
          <defs>
            <linearGradient id="splash-glow" x1="0%" y1="0%" x2="100%" y2="0%">
              <stop offset="0%" stopColor="#06b6d4" stopOpacity="0" />
              <stop offset="30%" stopColor="#38bdf8" stopOpacity="0.8" />
              <stop offset="70%" stopColor="#0284c7" stopOpacity="0.9" />
              <stop offset="100%" stopColor="#0284c7" stopOpacity="0" />
            </linearGradient>
          </defs>
        </svg>

        <div
          className="absolute right-0 top-12 w-[620px] h-[440px] opacity-25 rounded-3xl overflow-hidden bg-cover bg-center mix-blend-screen pointer-events-none"
          style={{ backgroundImage: `url('/assets/7.webp')`, backgroundPosition: 'right 30% top 10%' }}
        />
      </div>

      <div className="relative z-10 flex items-center justify-between">
        <div className="flex items-center gap-2 text-xs font-medium text-slate-400">
          <div className="w-2 h-2 rounded-full bg-cyan-400 animate-pulse" />
          <span>TravelCut Studio</span>
        </div>
      </div>

      <div className="relative z-10 max-w-2xl mx-auto text-center flex flex-col items-center">
        <div className="relative mb-6">
          <div className="w-24 h-24 rounded-3xl bg-gradient-to-tr from-cyan-500 via-blue-600 to-indigo-600 flex items-center justify-center shadow-2xl shadow-cyan-500/30 border border-cyan-400/30">
            <Play className="w-11 h-11 text-white fill-white ml-1.5" />
          </div>
          <div className="absolute -inset-2 rounded-3xl bg-cyan-400/20 blur-xl -z-10" />
        </div>

        <h1 className="text-5xl font-black tracking-tight text-white mb-2">
          TravelCut <span className="text-cyan-400">Studio</span>
        </h1>
        <p className="text-lg text-slate-300 font-medium mb-12">
          Edição inteligente de viagens
        </p>

        <div className="w-full max-w-xl text-left mb-8" role="status" aria-live="polite">
          <div className="text-base font-semibold text-slate-100 mb-2">Iniciando…</div>
          {/* Barra indeterminada: sem percentual, pois não há progresso real a medir. */}
          <div className="relative w-full h-2.5 bg-slate-900/90 rounded-full overflow-hidden border border-slate-800">
            <div className="absolute inset-y-0 w-1/3 rounded-full bg-gradient-to-r from-cyan-400 via-sky-400 to-blue-500 animate-pulse" />
          </div>
        </div>
      </div>

      <div className="relative z-10 flex flex-col items-center gap-3">
        <div className="flex items-center gap-2">
          <div className="w-2 h-2 rounded-full bg-slate-700" />
          <div className="w-5 h-2 rounded-full bg-cyan-400" />
          <div className="w-2 h-2 rounded-full bg-slate-700" />
        </div>
        <button
          onClick={onFinish}
          className="text-xs text-slate-400 hover:text-cyan-300 transition-colors underline-offset-4 hover:underline"
        >
          Pular inicialização e ir para o painel principal
        </button>
      </div>
    </div>
  );
};
