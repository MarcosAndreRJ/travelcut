import { describe, it, expect, beforeEach } from 'vitest';
import {
  isTauriDesktop,
  getDesktopDiagnostics,
  mediaGetCapabilities,
  mediaProbe,
  mediaScanDirectory,
  saveProjectState,
  loadProjectState,
  getRecentProjects,
  addRecentProject,
  removeRecentProject,
  RecentProjectItem,
  ProjectStatePayload,
} from '../services/desktopBridge';

describe('desktopBridge - Web and Tauri fallback behavior', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('detects runtime environment correctly as web without Tauri globals', () => {
    expect(isTauriDesktop()).toBe(false);
  });

  it('provides system diagnostics with platform and architecture', async () => {
    const diag = await getDesktopDiagnostics();
    expect(diag).toBeDefined();
    expect(diag.app_name).toContain('TravelCut Studio');
    expect(typeof diag.os).toBe('string');
    expect(typeof diag.cpu_architecture).toBe('string');
  });

  it('provides media engine capabilities in fallback mode', async () => {
    const caps = await mediaGetCapabilities();
    expect(caps).toBeDefined();
    expect(Array.isArray(caps.decoders)).toBe(true);
    expect(Array.isArray(caps.encoders)).toBe(true);
    expect(Array.isArray(caps.hwaccels)).toBe(true);
    expect(typeof caps.has_ffmpeg).toBe('boolean');
  });

  it('probes media files and returns metadata structure', async () => {
    const meta = await mediaProbe('D:\\Videos\\teste.mp4');
    expect(meta).toBeDefined();
    expect(meta.duration_secs).toBeGreaterThan(0);
    expect(meta.video).toBeDefined();
    expect(meta.video?.width).toBe(1920);
    expect(meta.video?.height).toBe(1080);
    expect(meta.format_name).toContain('mp4');
  });

  it('scans media directories and returns discovered files array', async () => {
    const files = await mediaScanDirectory('D:\\Videos\\Projeto', { recursive: true });
    expect(Array.isArray(files)).toBe(true);
  });
});

describe('desktopBridge - Project persistence and recent projects', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('saves and retrieves project state via localStorage fallback', async () => {
    const mockPayload: ProjectStatePayload = {
      version: '1.0.0',
      updated_at: new Date().toISOString(),
      project_name: 'Expedição Jalapão',
      root_directory: 'D:\\Videos\\Jalapao',
      timeline_data: {},
      media_pool: [],
      metadata: {
        mediaCount: 12,
        totalDurationSeconds: 720,
      },
    };

    const saved = await saveProjectState('D:\\Videos\\Jalapao', mockPayload);
    expect(saved).toBe(true);

    const loaded = await loadProjectState('D:\\Videos\\Jalapao');
    expect(loaded).toBeDefined();
    expect(loaded?.project_name).toBe('Expedição Jalapão');
    expect(loaded?.root_directory).toBe('D:\\Videos\\Jalapao');
    expect(loaded?.metadata?.mediaCount).toBe(12);
  });

  it('manages recent projects list with add and remove operations', async () => {
    const item1: RecentProjectItem = {
      id: 'proj-1',
      name: 'Roraima 2024',
      path: 'D:\\Videos\\Roraima',
      lastOpened: new Date().toISOString(),
      mediaCount: 48,
      totalDurationSeconds: 3600,
      previewThumbnail: null,
    };

    const item2: RecentProjectItem = {
      id: 'proj-2',
      name: 'Chapada Diamantina',
      path: 'D:\\Videos\\Chapada',
      lastOpened: new Date().toISOString(),
      mediaCount: 30,
      totalDurationSeconds: 1800,
      previewThumbnail: null,
    };

    await addRecentProject(item1);
    await addRecentProject(item2);

    let recents = await getRecentProjects();
    expect(recents.length).toBe(2);
    // item2 was added last, so it should be at the front
    expect(recents[0].name).toBe('Chapada Diamantina');

    await removeRecentProject('D:\\Videos\\Roraima');
    recents = await getRecentProjects();
    expect(recents.length).toBe(1);
    expect(recents[0].name).toBe('Chapada Diamantina');
  });
});
