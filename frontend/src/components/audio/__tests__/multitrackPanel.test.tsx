import { describe, it, expect, vi } from 'vitest';
import React from 'react';
import { renderToString } from 'react-dom/server';
import { MultitrackPlanningPanel } from '../MultitrackPlanningPanel';
import type { EditPlanDto, PlanClipDto } from '../../../types/ipc';
import { DEFAULT_TRACK_STATES } from '../multitrackModel';

const basePlan: EditPlanDto = {
  schemaVersion: 'editplan/1',
  projectId: 'p1',
  profileId: 'default',
  revision: 1,
  baseRevision: null,
  createdAtUtc: '2026-11-07T12:00:00Z',
  author: 'test',
  planFingerprint: 'fp',
  timelineTimeBase: { fpsNumerator: 30000, fpsDenominator: 1001, dropFrame: true },
  timelineDurationFrames: 300,
  tracks: [
    { trackId: 'V1', kind: 'video', label: 'V1 — vídeo' },
    { trackId: 'A1', kind: 'dialogue', label: 'A1 — áudio (diálogo)' }
  ],
  sections: [],
  clips: [
    {
      clipId: 'c1-v1',
      trackId: 'V1',
      mediaId: 'm1',
      relativePath: '1-Bruto/m1.mp4',
      sectionId: null,
      sourceInUs: 0,
      sourceOutUs: 5000000,
      timelineStartFrame: 0,
      durationFrames: 150,
      speedNum: 1,
      speedDen: 1,
      linkedClipId: 'c1-a1',
      needsHumanReview: false,
      reviewReason: null
    },
    {
      clipId: 'c1-a1',
      trackId: 'A1',
      mediaId: 'm1',
      relativePath: '1-Bruto/m1.mp4',
      sectionId: null,
      sourceInUs: 0,
      sourceOutUs: 5000000,
      timelineStartFrame: 0,
      durationFrames: 150,
      speedNum: 1,
      speedDen: 1,
      linkedClipId: 'c1-v1',
      needsHumanReview: false,
      reviewReason: null
    }
  ],
  transitions: [],
  issues: [],
  isValid: true
};

describe('MultitrackPlanningPanel', () => {
  it('renderiza o painel com as trilhas declaradas e abas', () => {
    const html = renderToString(
      <MultitrackPlanningPanel
        plan={basePlan}
        trackStates={DEFAULT_TRACK_STATES}
        selectedClipId={null}
        onPlanChange={vi.fn()}
        onTrackStatesChange={vi.fn()}
        onSelectClip={vi.fn()}
      />
    );
    expect(html).toContain('Trilhas (');
    expect(html).toContain('+ Vídeo');
    expect(html).toContain('+ Áudio');
    expect(html).toContain('Exportação');
  });

  it('renderiza aba de clipe selecionado com estado de vinculação', () => {
    const html = renderToString(
      <MultitrackPlanningPanel
        plan={basePlan}
        trackStates={DEFAULT_TRACK_STATES}
        selectedClipId="c1-v1"
        initialTab="clip"
        onPlanChange={vi.fn()}
        onTrackStatesChange={vi.fn()}
        onSelectClip={vi.fn()}
      />
    );
    expect(html).toContain('VINCULADO');
    expect(html).toContain('Desvincular áudio');
  });
});
