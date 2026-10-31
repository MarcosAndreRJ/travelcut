import { describe, it, expect, beforeEach } from 'vitest';
import {
  classifyMediaSegments,
  saveMediaSegments,
  loadMediaSegments
} from '../services/desktopBridge';
import { MediaSegment, MediaMetadata } from '../types/mediaEngine';

describe('Media Classification Engine & Segmentation', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('classifies speech segments when voice activity density exceeds threshold', () => {
    const metadata: Partial<MediaMetadata> = {
      duration_secs: 20,
      file_path: 'C:/Media/Entrevista_Guia.mp4',
      camera: { make: 'Sony', model: 'A7SIII' }
    };

    const vadSegments = [
      { startSec: 2, endSec: 10, hasSpeech: true },
      { startSec: 12, endSec: 18, hasSpeech: true }
    ];

    const segments = classifyMediaSegments('asset_interview_01', metadata, vadSegments);

    expect(segments.length).toBeGreaterThan(0);
    const speechSegments = segments.filter((s) => s.category === 'speech');
    expect(speechSegments.length).toBeGreaterThan(0);
    expect(speechSegments[0].confidence).toBeGreaterThanOrEqual(0.7);
  });

  it('classifies drone footage accurately based on camera make and model metadata', () => {
    const metadata: Partial<MediaMetadata> = {
      duration_secs: 30,
      file_path: 'C:/Media/DJI_0042.MP4',
      camera: { make: 'DJI', model: 'FC3582 (Mavic 3 Pro)' }
    };

    const segments = classifyMediaSegments('asset_drone_01', metadata);

    expect(segments.length).toBeGreaterThan(0);
    expect(segments.every((s) => s.category === 'drone')).toBe(true);
    expect(segments[0].confidence).toBeGreaterThanOrEqual(0.9);
  });

  it('classifies road trip footage based on context and filename heuristics', () => {
    const metadata: Partial<MediaMetadata> = {
      duration_secs: 45,
      file_path: 'C:/Media/Deslocamento_Savana_Drive.mp4',
      camera: { make: 'GoPro', model: 'HERO12 Black' }
    };

    const segments = classifyMediaSegments('asset_roadtrip_01', metadata);

    expect(segments.length).toBeGreaterThan(0);
    expect(segments[0].category).toBe('road_trip');
    expect(segments[0].confidence).toBeGreaterThanOrEqual(0.8);
  });

  it('classifies short cut takes as b-roll', () => {
    const metadata: Partial<MediaMetadata> = {
      duration_secs: 4.5,
      file_path: 'C:/Media/Detalhe_Planta.mp4',
      camera: { make: 'Canon', model: 'EOS R5' }
    };

    const segments = classifyMediaSegments('asset_broll_01', metadata);

    expect(segments.length).toBe(1);
    expect(segments[0].category).toBe('broll');
  });

  it('respects and preserves user-overridden segments during re-classification', () => {
    const metadata: Partial<MediaMetadata> = {
      duration_secs: 20,
      file_path: 'C:/Media/Paisagem_Com_Fala_Fundo.mp4',
      camera: { make: 'Sony', model: 'FX3' }
    };

    // User previously marked this segment as landscape override
    const existingSegments: MediaSegment[] = [
      {
        id: 'seg_custom_1',
        assetId: 'asset_override_01',
        startSec: 0,
        endSec: 10,
        category: 'landscape',
        confidence: 1.0,
        isUserOverridden: true,
        label: 'Aprovado pelo editor como Paisagem'
      }
    ];

    const vadSegments = [
      { startSec: 0, endSec: 10, hasSpeech: true } // VAD would otherwise mark it as speech
    ];

    const updated = classifyMediaSegments(
      'asset_override_01',
      metadata,
      vadSegments,
      [10],
      existingSegments
    );

    const firstSeg = updated.find((s) => s.startSec === 0 && s.endSec === 10);
    expect(firstSeg).toBeDefined();
    expect(firstSeg?.isUserOverridden).toBe(true);
    expect(firstSeg?.category).toBe('landscape');
  });

  it('saves and loads classified segments to/from storage', async () => {
    const assetId = 'asset_storage_test_01';
    const projectPath = 'K:/Projects/Roraima';

    const testSegments: MediaSegment[] = [
      {
        id: 'seg_1',
        assetId,
        startSec: 0,
        endSec: 8.5,
        category: 'drone',
        confidence: 0.95,
        isUserOverridden: false
      },
      {
        id: 'seg_2',
        assetId,
        startSec: 8.5,
        endSec: 22.0,
        category: 'speech',
        confidence: 0.92,
        isUserOverridden: true,
        label: 'Depoimento principal'
      }
    ];

    await saveMediaSegments(projectPath, assetId, testSegments);

    const loaded = await loadMediaSegments(projectPath, assetId);
    expect(loaded).toBeDefined();
    expect(loaded?.length).toBe(2);
    expect(loaded?.[0].category).toBe('drone');
    expect(loaded?.[1].category).toBe('speech');
    expect(loaded?.[1].isUserOverridden).toBe(true);
    expect(loaded?.[1].label).toBe('Depoimento principal');
  });
});
