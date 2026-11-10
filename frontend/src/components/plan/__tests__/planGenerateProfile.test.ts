import { describe, expect, it } from 'vitest';
import type { EditorialProfileDto } from '../../../types/ipc';
import { toGenerateProfile } from '../../../services/ipcBridge';

const full = {
  schemaVersion: '1',
  profileId: 'viagens',
  version: 1,
  silencePolicy: 'balanced',
  pace: 'normal',
  jokePreservation: 'normal',
  zoomStrength: 'subtle',
  speedBroll: 'off',
  musicPolicy: 'suggest_only',
  reviewAmbiguous: true,
  autoApplyTrivialCuts: true,
} as unknown as EditorialProfileDto;

describe('toGenerateProfile (contrato C# EditorialProfileDto)', () => {
  it('envia exatamente os 10 campos do C#, sem campos extras', () => {
    const out = toGenerateProfile(full)!;
    expect(Object.keys(out).sort()).toEqual(
      [
        'autoApplyTrivialCuts', 'jokePreservation', 'musicPolicy', 'pace', 'profileId',
        'reviewAmbiguous', 'silencePolicy', 'speedBroll', 'version', 'zoomStrength',
      ].sort(),
    );
    expect(out.profileId).toBe('viagens');
    expect(out.version).toBe(1);
    expect(out.autoApplyTrivialCuts).toBe(true);
  });

  it('reviewAmbiguous é sempre true na v1', () => {
    expect(toGenerateProfile({ ...full, reviewAmbiguous: false } as EditorialProfileDto)!.reviewAmbiguous).toBe(true);
  });

  it('autoApplyTrivialCuts ausente vira false', () => {
    const { autoApplyTrivialCuts: _omit, ...rest } = full as unknown as Record<string, unknown>;
    expect(toGenerateProfile(rest as unknown as EditorialProfileDto)!.autoApplyTrivialCuts).toBe(false);
  });

  it('perfil incompleto ou ausente vira null (host usa o perfil persistido)', () => {
    expect(toGenerateProfile(null)).toBeNull();
    expect(toGenerateProfile(undefined)).toBeNull();
    const { version: _v, ...noVersion } = full as unknown as Record<string, unknown>;
    expect(toGenerateProfile(noVersion as unknown as EditorialProfileDto)).toBeNull();
  });
});
