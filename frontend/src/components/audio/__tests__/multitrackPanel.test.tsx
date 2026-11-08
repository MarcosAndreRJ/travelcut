import { describe, it, expect, vi } from 'vitest';
import React from 'react';
import { renderToString } from 'react-dom/server';
import { MultitrackPlanningPanel } from '../MultitrackPlanningPanel';
import type { EditPlanDto, PlanClipDto } from '../../../types/ipc';
import { DEFAULT_TRACK_STATES } from '../multitrackModel';

const basePlan: any = {
  schemaVersion: 'editplan/1',
  projectId: 'p1',
  planId: 'p1',
  revision: 1,
  baseRevision: null,
  createdAtUtc: '2026-11-07T12:00:00Z',
  author: 'test',
  planFingerprint: 'fp',
  mediaSetFingerprint: 'fp',
  timeZoneId: 'UTC',
  timeBase: { frameDurationNum: 1001, frameDurationDen: 30000, label: '29.97 fps' },
  timelineDurationFrames: 300,
  tracks: [
    { trackId: 'V1', kind: 'video', label: 'V1 — vídeo' },
    { trackId: 'A1', kind: 'dialogue', label: 'A1 — áudio (diálogo)' }
  ],
  sections: [],
  clips: [],
  transitions: [],
  appliedCuts: [],
  pending: [],
  summary: {},
  validation: { isValid: true, issues: [], validatedAtUtc: '' },
  profile: { profileId: 'default' }
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
