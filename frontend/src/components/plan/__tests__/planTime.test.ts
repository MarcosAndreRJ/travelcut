import { describe, expect, it } from 'vitest';
import type { TimelineTimeBaseDto } from '../../../types/ipc';
import {
  formatDuration,
  formatSeconds3,
  formatSourceTimecode,
  formatSpeed,
  formatTimecode,
  framesToSeconds,
  isNormalSpeed,
  isValidTimeBase,
  nominalFps,
  secondsToFrames,
  usToSeconds
} from '../planTime';

const TB_2997: TimelineTimeBaseDto = { frameDurationNum: 1001, frameDurationDen: 30000, label: '29,97 fps' };
const TB_5994: TimelineTimeBaseDto = { frameDurationNum: 1001, frameDurationDen: 60000, label: '59,94 fps' };
const TB_23976: TimelineTimeBaseDto = { frameDurationNum: 1001, frameDurationDen: 24000, label: '23,976 fps' };
const TB_25: TimelineTimeBaseDto = { frameDurationNum: 1, frameDurationDen: 25, label: '25 fps' };

describe('planTime: base de tempo', () => {
  it('aceita somente frações positivas', () => {
    expect(isValidTimeBase(TB_2997)).toBe(true);
    expect(isValidTimeBase({ frameDurationNum: 0, frameDurationDen: 30, label: 'x' })).toBe(false);
    expect(isValidTimeBase({ frameDurationNum: 1, frameDurationDen: -1, label: 'x' })).toBe(false);
    expect(isValidTimeBase(null)).toBe(false);
  });

  it('fps nominal arredonda 29,97 → 30, 59,94 → 60 e 23,976 → 24', () => {
    expect(nominalFps(TB_2997)).toBe(30);
    expect(nominalFps(TB_5994)).toBe(60);
    expect(nominalFps(TB_23976)).toBe(24);
    expect(nominalFps(TB_25)).toBe(25);
  });
});

describe('planTime: frames ↔ segundos reais', () => {
  it('29,97 (1001/30000): 1800 frames = 60,06 s reais', () => {
    expect(framesToSeconds(1800, TB_2997)).toBeCloseTo(60.06, 9);
    expect(secondsToFrames(60.06, TB_2997)).toBe(1800);
  });

  it('59,94 (1001/60000): 3600 frames = 60,06 s reais', () => {
    expect(framesToSeconds(3600, TB_5994)).toBeCloseTo(60.06, 9);
    expect(secondsToFrames(60.06, TB_5994)).toBe(3600);
  });

  it('23,976 (1001/24000): 1440 frames = 60,06 s reais', () => {
    expect(framesToSeconds(1440, TB_23976)).toBeCloseTo(60.06, 9);
    expect(secondsToFrames(60.06, TB_23976)).toBe(1440);
  });

  it('base inteira (25 fps) converte sem desvio', () => {
    expect(framesToSeconds(250, TB_25)).toBe(10);
    expect(secondsToFrames(10, TB_25)).toBe(250);
  });

  it('secondsToFrames arredonda para o frame mais próximo', () => {
    expect(secondsToFrames(1.02, TB_25)).toBe(26);
    expect(secondsToFrames(1.01, TB_25)).toBe(25);
  });

  it('timeBase inválido ou entrada não finita viram NaN (sem estimativa)', () => {
    const bad: TimelineTimeBaseDto = { frameDurationNum: 0, frameDurationDen: 0, label: 'x' };
    expect(framesToSeconds(10, bad)).toBeNaN();
    expect(secondsToFrames(10, bad)).toBeNaN();
    expect(framesToSeconds(Number.NaN, TB_2997)).toBeNaN();
  });

  it('usToSeconds converte microssegundos da fonte', () => {
    expect(usToSeconds(1_500_000)).toBe(1.5);
    expect(usToSeconds(Number.NaN)).toBeNaN();
  });
});

describe('planTime: timecode NDF', () => {
  it('29,97 usa rótulo NDF de 30 quadros por segundo de rótulo', () => {
    expect(formatTimecode(1800, TB_2997)).toBe('00:01:00:00');
    expect(formatTimecode(1799, TB_2997)).toBe('00:00:59:29');
  });

  it('59,94 e 23,976 seguem o fps nominal (60 e 24)', () => {
    expect(formatTimecode(3600, TB_5994)).toBe('00:01:00:00');
    expect(formatTimecode(1440, TB_23976)).toBe('00:01:00:00');
    expect(formatTimecode(1439, TB_23976)).toBe('00:00:59:23');
  });

  it('horas são exibidas com dois dígitos', () => {
    // 2 h + 61 s + 5 frames, em 30 fps nominais.
    expect(formatTimecode(30 * 3600 * 2 + 30 * 61 + 5, { frameDurationNum: 1, frameDurationDen: 30, label: '30' })).toBe(
      '02:01:01:05'
    );
  });

  it('valores inválidos viram placeholder', () => {
    expect(formatTimecode(10, { frameDurationNum: 0, frameDurationDen: 30, label: 'x' })).toBe('—');
    expect(formatTimecode(Number.NaN, TB_2997)).toBe('—');
  });

  it('timecode de fonte com milissegundos', () => {
    expect(formatSourceTimecode(3_723_004_000)).toBe('01:02:03.004');
    expect(formatSourceTimecode(Number.NaN)).toBe('—');
  });
});

describe('planTime: formatação de segundos, duração e velocidade', () => {
  it('segundos com três casas e vírgula PT-BR', () => {
    expect(formatSeconds3(12.3456)).toBe('12,346 s');
    expect(formatSeconds3(Number.NaN)).toBe('—');
  });

  it('duração legível em h/min/s', () => {
    expect(formatDuration(3725)).toBe('1h 02min 05s');
    expect(formatDuration(125)).toBe('2min 05s');
    expect(formatDuration(5)).toBe('5s');
    expect(formatDuration(-1)).toBe('—');
  });

  it('velocidade: inteiros, frações e razão quase 1 (1001/1000)', () => {
    expect(formatSpeed(2, 1)).toBe('2×');
    expect(formatSpeed(1, 2)).toBe('0,5×');
    expect(formatSpeed(3, 2)).toBe('1,5×');
    expect(formatSpeed(1001, 1000)).toBe('1,001×');
    expect(formatSpeed(0, 1)).toBe('—');
  });

  it('velocidade normal é quando num === den', () => {
    expect(isNormalSpeed(1, 1)).toBe(true);
    expect(isNormalSpeed(2, 1)).toBe(false);
    expect(isNormalSpeed(0, 0)).toBe(false);
  });
});
