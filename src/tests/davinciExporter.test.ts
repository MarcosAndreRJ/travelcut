import { describe, it, expect } from 'vitest';
import {
  secondsToTimecode,
  secondsToFrames,
  generateDaVinciXML,
  generateDaVinciEDL,
  computeExportData,
} from '../features/editing/utils/davinciExporter';
import {
  UnifiedMediaAsset,
  UnifiedTimelineClip,
  UnifiedCutSuggestion,
} from '../features/editing/types/editingTypes';

describe('davinciExporter - timecode and frame utilities', () => {
  it('converts seconds to SMPTE timecode (HH:MM:SS:FF)', () => {
    expect(secondsToTimecode(0, 60)).toBe('00:00:00:00');
    expect(secondsToTimecode(65.5, 60)).toBe('00:01:05:30');
    expect(secondsToTimecode(3661, 30)).toBe('01:01:01:00');
    expect(secondsToTimecode(-10, 60)).toBe('00:00:00:00');
  });

  it('converts seconds to frame count based on fps', () => {
    expect(secondsToFrames(0, 60)).toBe(0);
    expect(secondsToFrames(1, 60)).toBe(60);
    expect(secondsToFrames(1.5, 60)).toBe(90);
    expect(secondsToFrames(2, 30)).toBe(60);
    expect(secondsToFrames(-5, 60)).toBe(0);
  });
});

describe('davinciExporter - XML generation', () => {
  const mockAsset: UnifiedMediaAsset = {
    id: 'asset-1',
    fileName: 'GH010045.MP4',
    displayName: 'GH010045.MP4',
    mediaType: 'video',
    sourcePath: 'D:\\Videos\\GH010045.MP4',
    durationSec: 120,
    resolution: '3840x2160',
    frameRate: 60,
    recordedAt: '2026-10-29',
    device: 'GoPro HERO 12',
    thumbnail: '',
    category: 'A-Roll (Vlog)',
    location: 'Trilha',
    aiScore: 90,
    aspectRatio: '16:9',
    codec: 'H.265',
    colorProfile: 'Flat',
    stability: 'Gimbal Suave',
    lightingQuality: 'Perfeita',
    waveformPoints: [0.1, 0.4],
    transcription: [],
    cutSuggestions: [],
  };

  const mockClip: UnifiedTimelineClip = {
    id: 'clip-1',
    mediaClipId: 'asset-1',
    title: 'Cena da Trilha',
    timelineStartSec: 0,
    durationSec: 15,
    sourceInSec: 5,
    sourceOutSec: 20,
    trackIndex: 0,
    colorGradeLut: 'GoPro Natural',
    transition: 'none',
    device: 'GoPro HERO 12',
  };

  it('generates standard DaVinci Resolve / FCP7 XML structure', () => {
    const xml = generateDaVinciXML('Projeto Viagem', [mockClip], [mockAsset], 60);

    expect(xml).toContain('<?xml version="1.0" encoding="UTF-8"?>');
    expect(xml).toContain('<xmeml version="5">');
    expect(xml).toContain('<name>Projeto Viagem</name>');
    expect(xml).toContain('<clipitem id="clipitem-clip-1">');
    expect(xml).toContain('<name>Cena da Trilha</name>');
    expect(xml).toContain('<timebase>60</timebase>');
    expect(xml).toContain('file://localhost/D:/Videos/GH010045.MP4');
  });
});

describe('davinciExporter - EDL generation', () => {
  const mockAsset: UnifiedMediaAsset = {
    id: 'asset-1',
    fileName: 'DJI_0023.MP4',
    displayName: 'DJI_0023.MP4',
    mediaType: 'video',
    sourcePath: 'D:\\Videos\\DJI_0023.MP4',
    durationSec: 90,
    resolution: '3840x2160',
    frameRate: 30,
    recordedAt: '2026-10-29',
    device: 'DJI Mini 4 Pro',
    thumbnail: '',
    category: 'Drone Aéreo',
    location: 'Mirante',
    aiScore: 95,
    aspectRatio: '16:9',
    codec: 'H.265',
    colorProfile: 'D-Log M',
    stability: 'Tripé Fixo',
    lightingQuality: 'Perfeita',
    waveformPoints: [],
    transcription: [],
    cutSuggestions: [],
  };

  const mockClip: UnifiedTimelineClip = {
    id: 'clip-1',
    mediaClipId: 'asset-1',
    title: 'Voo Panorâmico',
    timelineStartSec: 0,
    durationSec: 10,
    sourceInSec: 0,
    sourceOutSec: 10,
    trackIndex: 0,
    colorGradeLut: 'D-Log M to Rec709',
    transition: 'none',
    device: 'DJI Mini 4 Pro',
  };

  it('generates CMX 3600 standard EDL output', () => {
    const edl = generateDaVinciEDL('Projeto Drone', [mockClip], [mockAsset], 30);

    expect(edl).toContain('TITLE: PROJETO DRONE');
    expect(edl).toContain('FCM: NON-DROP FRAME');
    expect(edl).toContain('001');
    expect(edl).toContain('V     C');
    expect(edl).toContain('* FROM CLIP: Voo Panorâmico');
  });
});

describe('davinciExporter - computeExportData analytics', () => {
  const mockAsset: UnifiedMediaAsset = {
    id: 'asset-1',
    fileName: 'VIDEO_01.MP4',
    displayName: 'VIDEO_01.MP4',
    mediaType: 'video',
    sourcePath: 'D:\\Videos\\VIDEO_01.MP4',
    durationSec: 100,
    resolution: '3840x2160',
    frameRate: 60,
    recordedAt: '2026-10-29',
    device: 'Sony A7IV',
    thumbnail: '',
    category: 'A-Roll (Vlog)',
    location: 'Estúdio',
    aiScore: 88,
    aspectRatio: '16:9',
    codec: 'H.264',
    colorProfile: 'S-Cinetone',
    stability: 'Tripé Fixo',
    lightingQuality: 'Boa',
    waveformPoints: [0.2, 0.6],
    transcription: [],
    cutSuggestions: [],
  };

  const mockClip: UnifiedTimelineClip = {
    id: 'clip-1',
    mediaClipId: 'asset-1',
    title: 'Trecho 1',
    timelineStartSec: 0,
    durationSec: 40,
    sourceInSec: 0,
    sourceOutSec: 40,
    trackIndex: 0,
    colorGradeLut: 'S-Cinetone',
    transition: 'none',
    device: 'Sony A7IV',
  };

  const mockCuts: UnifiedCutSuggestion[] = [
    {
      id: 'cut-1',
      mediaId: 'asset-1',
      clipTitle: 'Vídeo 01',
      startSec: 40,
      endSec: 70,
      durationSec: 30,
      confidence: 0.95,
      reason: 'silence',
      reasonLabel: 'Silêncio prolongado',
      decisionState: 'accepted',
      previewText: '...',
      timecodeFormatted: '00:00:40:00',
    },
    {
      id: 'cut-2',
      mediaId: 'asset-1',
      clipTitle: 'Vídeo 01',
      startSec: 70,
      endSec: 80,
      durationSec: 10,
      confidence: 0.6,
      reason: 'unstable',
      reasonLabel: 'Tremedeira',
      decisionState: 'rejected',
      previewText: '...',
      timecodeFormatted: '00:01:10:00',
    },
  ];

  it('computes correct saved seconds and metrics', () => {
    const data = computeExportData('Viagem 2026', [mockClip], [mockAsset], mockCuts, 60);

    expect(data.projectName).toBe('Viagem 2026');
    expect(data.totalOriginalSec).toBe(100);
    expect(data.totalEditedSec).toBe(40);
    expect(data.savedSeconds).toBe(30);
    expect(data.percentageSaved).toBe(30);
    expect(data.cutsAppliedCount).toBe(1);
    expect(data.cutsRejectedCount).toBe(1);
    expect(data.clipsCount).toBe(1);
    expect(data.xmlContent).toBeTruthy();
    expect(data.edlContent).toBeTruthy();
  });
});
