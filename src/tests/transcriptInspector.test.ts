import { describe, it, expect, beforeEach } from 'vitest';
import {
  runTranscription,
  extractAudioForTranscription,
  saveTranscription,
  loadTranscription,
  TranscriptionResult
} from '../services/desktopBridge';

describe('desktopBridge - Whisper Transcription integration', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('runs mock transcription successfully in fallback web mode', async () => {
    const result: TranscriptionResult = await runTranscription('audio.wav', 'pt', 'small');
    expect(result).toBeDefined();
    expect(result.segments).toBeDefined();
    expect(result.segments!.length).toBeGreaterThan(0);
    expect(result.segments![0].text).toBeTruthy();
    expect(result.segments![0].words).toBeDefined();
    expect(result.segments![0].words!.length).toBeGreaterThan(0);
    expect(result.segments![0].words![0].text).toBeTruthy();
    expect(result.segments![0].words![0].startSec).toBeGreaterThanOrEqual(0);
  });

  it('extracts audio placeholder for transcription', async () => {
    const audioPath = await extractAudioForTranscription('clip_001.mp4');
    expect(audioPath).toBeDefined();
    expect(audioPath).toContain('clip_001');
    expect(audioPath).toMatch(/\.wav$/);
  });

  it('saves and loads transcription for a project asset', async () => {
    const mockResult: TranscriptionResult = {
      mediaId: 'clip-roraima-01',
      durationMs: 12500,
      fullText: 'Explorando as savanas de Roraima ao nascer do sol.',
      language: 'pt',
      durationSec: 12.5,
      text: 'Explorando as savanas de Roraima ao nascer do sol.',
      segments: [
        {
          id: 'seg_01',
          startSec: 0,
          endSec: 5.5,
          text: 'Explorando as savanas de Roraima ao nascer do sol.',
          words: [
            { id: 'w1', text: 'Explorando', startSec: 0, endSec: 0.8, confidence: 0.98 },
            { id: 'w2', text: 'as', startSec: 0.85, endSec: 1.0, confidence: 0.99 },
            { id: 'w3', text: 'savanas', startSec: 1.05, endSec: 1.8, confidence: 0.97 },
            { id: 'w4', text: 'de', startSec: 1.85, endSec: 2.0, confidence: 0.99 },
            { id: 'w5', text: 'Roraima', startSec: 2.05, endSec: 2.9, confidence: 0.96 }
          ],
          isCutCandidate: false
        }
      ]
    };

    const projectDir = 'D:\\Projects\\Roraima2024';
    const assetId = 'clip-roraima-01';

    await saveTranscription(projectDir, assetId, mockResult);

    const loaded = await loadTranscription(projectDir, assetId);
    expect(loaded).toBeDefined();
    expect(loaded?.language).toBe('pt');
    expect(loaded?.segments?.length).toBe(1);
    expect(loaded?.segments?.[0]?.text).toBe('Explorando as savanas de Roraima ao nascer do sol.');
    expect(loaded?.segments?.[0]?.words?.length).toBe(5);
  });
});
