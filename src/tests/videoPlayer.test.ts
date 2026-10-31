import { describe, it, expect } from 'vitest';
import { secondsToTimecode } from '../features/editing/utils/davinciExporter';

// Funcao auxiliar de conversao SMPTE para teste
function timecodeToSeconds(tc: string, fps: number = 30): number | null {
  const parts = tc.trim().split(':');
  if (parts.length !== 4) return null;
  const [h, m, s, f] = parts.map((p) => parseInt(p, 10));
  if (isNaN(h) || isNaN(m) || isNaN(s) || isNaN(f)) return null;
  return h * 3600 + m * 60 + s + f / fps;
}

describe('Video Player SMPTE Timecode & Transport Calculations', () => {
  it('converte segundos para timecode SMPTE padrão HH:MM:SS:FF com 30fps', () => {
    expect(secondsToTimecode(0, 30)).toBe('00:00:00:00');
    expect(secondsToTimecode(1, 30)).toBe('00:00:01:00');
    expect(secondsToTimecode(65.5, 30)).toBe('00:01:05:15');
    expect(secondsToTimecode(3661.1, 30)).toBe('01:01:01:03');
  });

  it('converte segundos para timecode SMPTE com 60fps', () => {
    expect(secondsToTimecode(0, 60)).toBe('00:00:00:00');
    expect(secondsToTimecode(0.5, 60)).toBe('00:00:00:30');
    expect(secondsToTimecode(10.25, 60)).toBe('00:00:10:15');
  });

  it('faz o parse inverso de SMPTE para segundos corretamente', () => {
    const fps = 30;
    const tc = '00:01:05:15';
    const seconds = timecodeToSeconds(tc, fps);
    expect(seconds).toBeCloseTo(65.5, 2);

    expect(timecodeToSeconds('01:00:00:00', fps)).toBe(3600);
    expect(timecodeToSeconds('invalid', fps)).toBeNull();
  });

  it('calcula passo de 1 frame para frente e para trás corretamente', () => {
    const fps = 60;
    const frameSec = 1 / fps;
    const initialSec = 10.0;

    const nextFrame = initialSec + frameSec;
    const prevFrame = initialSec - frameSec;

    expect(nextFrame - initialSec).toBeCloseTo(0.016666, 4);
    expect(initialSec - prevFrame).toBeCloseTo(0.016666, 4);
    expect(secondsToTimecode(nextFrame, fps)).toBe('00:00:10:01');
  });
});
