import { renderToString } from 'react-dom/server';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PlaybackSourceDto } from '../../../types/ipc';
import { compatibilityHint, resolveClipNotice } from '../mediaClipModel';
import { afterModeSkipTarget, contextWindow } from '../mediaTime';
import { MediaIntervalPlayer } from '../MediaIntervalPlayer';
import type { PlaybackSourceState } from '../usePlaybackSource';

const hooks = vi.hoisted(() => ({
  state: { kind: 'loading' } as unknown,
  proxy: { phase: 'idle', message: null as string | null, busy: false, start: () => undefined }
}));

vi.mock('../usePlaybackSource', async (original) => ({
  ...(await original<typeof import('../usePlaybackSource')>()),
  usePlaybackSource: () => ({ state: hooks.state, reload: () => undefined })
}));
vi.mock('../useProxyGeneration', () => ({ useProxyGeneration: () => hooks.proxy }));

const source = (over: Partial<PlaybackSourceDto> = {}): PlaybackSourceDto => ({
  mediaId: 'm1',
  status: 'original',
  url: 'https://media.travelcut.studio/1-Bruto/IMG_0001.MOV',
  mimeType: 'video/quicktime',
  codec: 'hevc',
  likelyPlayable: null,
  proxyStatus: 'none',
  reason: null,
  ...over
});
const ready = (over: Partial<PlaybackSourceDto> = {}): PlaybackSourceState => ({ kind: 'ready', source: source(over) });

const render = (mode: 'before' | 'after' = 'before'): string =>
  renderToString(<MediaIntervalPlayer projectId="p1" mediaId="m1" startSeconds={12} endSeconds={12.1} mode={mode} videoAreaClassName="h-[32vh]" />);

describe('MediaIntervalPlayer (revisão)', () => {
  beforeEach(() => {
    hooks.proxy = { phase: 'idle', message: null, busy: false, start: () => undefined };
  });

  it('HEVC de compatibilidade desconhecida já oferece o proxy (não fica só a mensagem genérica)', () => {
    hooks.state = ready();
    const html = render();
    expect(html).toContain('Suporte ao codec não confirmado');
    expect(html).toContain('gere o proxy');
    expect(html).toContain('Gerar proxy compatível');
  });

  it('codec provavelmente incompatível oferece o proxy', () => {
    hooks.state = ready({ likelyPlayable: false, codec: 'prores' });
    expect(render()).toContain('Codec provavelmente sem suporte');
  });

  it('fonte compatível não mostra aviso nem proxy', () => {
    hooks.state = ready({ codec: 'h264', likelyPlayable: true, mimeType: 'video/mp4' });
    const html = render();
    expect(html).not.toContain('Gerar proxy compatível');
    expect(html).toContain('src="https://media.travelcut.studio/1-Bruto/IMG_0001.MOV"');
  });

  it('fonte indisponível usa o mesmo aviso do player de clipe', () => {
    hooks.state = ready({ status: 'unavailable', url: null, reason: 'Formato BRAW não reproduzível.' });
    const html = render();
    expect(html).toContain('data-testid="player-notice-unavailable"');
    expect(html).toContain('Formato BRAW não reproduzível.');
    expect(html).toContain('arquivo original não é alterado');
  });

  it('proxy em geração, pronto e reproduzindo', () => {
    hooks.state = ready({ proxyStatus: 'generating' });
    hooks.proxy = { ...hooks.proxy, busy: true };
    expect(render()).toContain('Gerando proxy…');

    hooks.proxy = { ...hooks.proxy, busy: false };
    hooks.state = ready({ status: 'proxy', proxyStatus: 'ready', codec: 'h264', likelyPlayable: true });
    const html = render();
    // F13.5: antes de tocar, indica proxy pronto
    expect(html).toContain('Proxy pronto');
    expect(html).not.toContain('Gerar proxy compatível');
  });

  it('falha anterior da geração aparece', () => {
    hooks.state = ready({ proxyStatus: 'failed', codec: 'h264', likelyPlayable: true });
    expect(render()).toContain('A última geração de proxy falhou');
  });

  it('altura do vídeo limitada pela revisão e marcador do intervalo curto', () => {
    hooks.state = ready({ codec: 'h264', likelyPlayable: true });
    const html = render('after');
    expect(html).toContain('h-[32vh]');
    expect(html).not.toContain('aspect-video');
    expect(html).toContain('Pós-corte: trecho removido é pulado');
    expect(html).toContain('não frame-accurate');
  });
});

describe('diagnóstico compartilhado', () => {
  const base = { hasMediaError: false, mediaErrorCode: null, noVideoTrack: false };

  it('HEVC só com áudio cobre a tela preta com causa e proxy', () => {
    const notice = resolveClipNotice({ ...base, state: ready(), noVideoTrack: true });
    expect(notice.kind).toBe('no_video_track');
    expect(notice.kind === 'no_video_track' && notice.offerProxy).toBe(true);
    expect(compatibilityHint(source(), notice)).toBeNull();
  });

  it('erro do elemento oferece proxy tanto para original quanto para regeneração de proxy corrompido', () => {
    // F13.5: original com erro oferece proxy; proxy com erro oferece regeneracao
    expect(resolveClipNotice({ ...base, state: ready(), hasMediaError: true, mediaErrorCode: 4 })).toMatchObject({ offerProxy: true });
    expect(resolveClipNotice({ ...base, state: ready({ status: 'proxy' }), hasMediaError: true, mediaErrorCode: 3 })).toMatchObject({ offerProxy: true });
  });

  it('sem FFmpeg não há proxy em nenhum aviso', () => {
    const notice = resolveClipNotice({ ...base, state: ready({ proxyStatus: 'ffmpeg_unavailable' }), noVideoTrack: true });
    expect(notice.kind === 'no_video_track' && notice.offerProxy).toBe(false);
    expect(compatibilityHint(source({ proxyStatus: 'ffmpeg_unavailable' }), { kind: 'none' })?.offerProxy).toBe(false);
  });
});

describe('Antes/Depois em intervalos curtos', () => {
  it('0,1 s: janela com contexto e salto exato do trecho', () => {
    expect(contextWindow(12, 12.1, 2, 60)).toEqual({ start: 10, end: 14.1 });
    expect(afterModeSkipTarget(12, 12, 12.1)).toBe(12.1);
    expect(afterModeSkipTarget(12.05, 12, 12.1)).toBe(12.1);
    expect(afterModeSkipTarget(12.1, 12, 12.1)).toBeNull();
    expect(afterModeSkipTarget(11.99, 12, 12.1)).toBeNull();
  });

  it('0,6 s e intervalo modificado mudam só a janela e o salto', () => {
    expect(contextWindow(5.2, 5.8, 2, 60)).toEqual({ start: 3.2, end: 7.8 });
    expect(afterModeSkipTarget(5.5, 5.2, 5.8)).toBe(5.8);
    // Intervalo ajustado pelo usuário (modify): o mesmo cálculo com os novos limites.
    expect(contextWindow(5.0, 6.1, 2, 60)).toEqual({ start: 3, end: 8.1 });
    expect(afterModeSkipTarget(6.0, 5.0, 6.1)).toBe(6.1);
  });

  it('perto do início do arquivo o contexto não fica negativo', () => {
    expect(contextWindow(0.3, 0.4, 2, 60).start).toBe(0);
  });
});
