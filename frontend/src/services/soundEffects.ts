// Efeitos sonoros procedurais via Web Audio API (sem dependência de arquivos externos).
// Discretos, suaves, de baixa latência e frequências harmônicas agradáveis.

let audioCtx: AudioContext | null = null;
let lastSoundTime = 0;
const DEBOUNCE_MS = 3000;

function getAudioContext(): AudioContext | null {
  if (typeof window === 'undefined') return null;
  const AudioContextClass = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
  if (!AudioContextClass) return null;
  if (!audioCtx) {
    audioCtx = new AudioContextClass();
  }
  if (audioCtx.state === 'suspended') {
    void audioCtx.resume();
  }
  return audioCtx;
}

export function isSoundEnabled(): boolean {
  if (typeof window === 'undefined') return false;
  return localStorage.getItem('travelcut_sound_enabled') !== 'false';
}

export function setSoundEnabled(enabled: boolean): void {
  if (typeof window === 'undefined') return;
  localStorage.setItem('travelcut_sound_enabled', enabled ? 'true' : 'false');
}

export function getSoundVolume(): number {
  if (typeof window === 'undefined') return 0.7;
  const val = parseFloat(localStorage.getItem('travelcut_sound_volume') || '0.7');
  return Number.isFinite(val) ? Math.max(0, Math.min(1, val)) : 0.7;
}

export function setSoundVolume(volume: number): void {
  if (typeof window === 'undefined') return;
  const clamped = Math.max(0, Math.min(1, volume));
  localStorage.setItem('travelcut_sound_volume', clamped.toString());
}

/** Chime duplo harmônico suave para conclusão bem-sucedida (520 Hz -> 660 Hz). */
export function playSuccessChime(): void {
  if (!isSoundEnabled()) return;
  const now = Date.now();
  if (now - lastSoundTime < DEBOUNCE_MS) return;
  lastSoundTime = now;

  const ctx = getAudioContext();
  if (!ctx) return;

  const masterVol = getSoundVolume();
  const startTime = ctx.currentTime + 0.02;

  // Primeiro tom (C5 ~ 523 Hz)
  const osc1 = ctx.createOscillator();
  const gain1 = ctx.createGain();
  osc1.type = 'sine';
  osc1.frequency.setValueAtTime(523.25, startTime);
  gain1.gain.setValueAtTime(0.001, startTime);
  gain1.gain.exponentialRampToValueAtTime(0.18 * masterVol, startTime + 0.02);
  gain1.gain.exponentialRampToValueAtTime(0.0001, startTime + 0.22);
  osc1.connect(gain1);
  gain1.connect(ctx.destination);
  osc1.start(startTime);
  osc1.stop(startTime + 0.25);

  // Segundo tom harmônico (E5 ~ 659 Hz) com pequeno atraso
  const t2 = startTime + 0.08;
  const osc2 = ctx.createOscillator();
  const gain2 = ctx.createGain();
  osc2.type = 'sine';
  osc2.frequency.setValueAtTime(659.25, t2);
  gain2.gain.setValueAtTime(0.001, t2);
  gain2.gain.exponentialRampToValueAtTime(0.22 * masterVol, t2 + 0.02);
  gain2.gain.exponentialRampToValueAtTime(0.0001, t2 + 0.28);
  osc2.connect(gain2);
  gain2.connect(ctx.destination);
  osc2.start(t2);
  osc2.stop(t2 + 0.3);
}

/** Tom descendente suave para atenção ou falha de processamento (440 Hz -> 330 Hz). */
export function playFailureChime(): void {
  if (!isSoundEnabled()) return;
  const now = Date.now();
  if (now - lastSoundTime < DEBOUNCE_MS) return;
  lastSoundTime = now;

  const ctx = getAudioContext();
  if (!ctx) return;

  const masterVol = getSoundVolume();
  const startTime = ctx.currentTime + 0.02;

  const osc = ctx.createOscillator();
  const gain = ctx.createGain();
  osc.type = 'sine';
  osc.frequency.setValueAtTime(440, startTime);
  osc.frequency.exponentialRampToValueAtTime(329.63, startTime + 0.25);

  gain.gain.setValueAtTime(0.001, startTime);
  gain.gain.exponentialRampToValueAtTime(0.16 * masterVol, startTime + 0.02);
  gain.gain.exponentialRampToValueAtTime(0.0001, startTime + 0.3);

  osc.connect(gain);
  gain.connect(ctx.destination);
  osc.start(startTime);
  osc.stop(startTime + 0.32);
}
