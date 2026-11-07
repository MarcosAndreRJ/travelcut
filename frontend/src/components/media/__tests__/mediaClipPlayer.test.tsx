import { renderToString } from 'react-dom/server';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PlaybackSourceDto } from '../../../types/ipc';
import { clampSeek, formatPlayerTime, isAudioAsset, isImageAsset, proxyIndicator, resolveClipNotice, stripPositionToSeconds } from '../mediaClipModel';
import { MediaClipPlayer } from '../MediaClipPlayer';
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
  url: 'https://media.travelcut.studio/1-Bruto/clipe.mp4',
  mimeType: 'video/mp4',
  codec: 'h264',
  likelyPlayable: true,
  proxyStatus: 'none',
  reason: null,
  ...over
});

const ready = (over: Partial<PlaybackSourceDto> = {}): PlaybackSourceState => ({ kind: 'ready', source: source(over) });
const render = (): string => renderToString(<MediaClipPlayer projectId="p1" mediaId="m1" durationHint={125} />);

describe('MediaClipPlayer', () => {
  beforeEach(() => {
    hooks.proxy = { phase: 'idle', message: null, busy: false, start: () => undefined };
  });

  it('carrega a URL autorizada do host, sem autoplay e sem file://', () => {
    hooks.state = ready();
    const html = render();
    expect(html).toContain('src="https://media.travelcut.studio/1-Bruto/clipe.mp4"');
    expect(html).toContain('preload="metadata"');
    expect(html).not.toContain('autoplay');
    expect(html).not.toContain('file://');
    expect(html).not.toContain('crossorigin');
  });

  it('oferece reprodução, busca, volume, silenciar, tempo e tela cheia', () => {
    hooks.state = ready();
    const html = render();
    for (const label of ['Reproduzir', 'Posição no clipe', 'Silenciar', 'Volume', 'Tela cheia']) {
      expect(html).toContain(`aria-label="${label}"`);
    }
    expect(html).toContain('00:00');
    expect(html).toContain('02:05');
  });

  it('mostra carregamento', () => {
    hooks.state = { kind: 'loading' };
    expect(render()).toContain('Carregando fonte de reprodução');
  });

  it('erro ao obter a fonte oferece nova tentativa', () => {
    hooks.state = { kind: 'error', message: 'Projeto não encontrado' };
    const html = render();
    expect(html).toContain('Projeto não encontrado');
    expect(html).toContain('Tentar novamente');
  });

  it('fonte indisponível explica o motivo e oferece proxy', () => {
    hooks.state = ready({ status: 'unavailable', url: null, reason: 'Formato ProRes não reproduzível.' });
    const html = render();
    expect(html).toContain('Formato ProRes não reproduzível.');
    expect(html).toContain('Gerar proxy compatível');
    expect(html).toContain('arquivo original não é alterado');
  });

  it('sem FFmpeg não oferece proxy', () => {
    hooks.state = ready({ status: 'unavailable', url: null, proxyStatus: 'ffmpeg_unavailable', reason: 'FFmpeg ausente.' });
    expect(render()).not.toContain('Gerar proxy compatível');
  });

  it('indica geração e uso do proxy', () => {
    hooks.state = ready({ status: 'unavailable', url: null, proxyStatus: 'generating' });
    hooks.proxy = { ...hooks.proxy, busy: true };
    expect(render()).toContain('Gerando proxy…');

    hooks.proxy = { ...hooks.proxy, busy: false };
    hooks.state = ready({ status: 'proxy', proxyStatus: 'ready' });
    expect(render()).toContain('Reproduzindo proxy');
  });
});

describe('resolveClipNotice', () => {
  const base = { hasMediaError: false, mediaErrorCode: null, noVideoTrack: false };

  it('fonte tocável sem erro não cobre o vídeo', () => {
    expect(resolveClipNotice({ ...base, state: ready() }).kind).toBe('none');
  });

  it('falha de codec do elemento identifica a incompatibilidade e oferece proxy', () => {
    const notice = resolveClipNotice({ ...base, state: ready({ codec: 'hevc' }), hasMediaError: true, mediaErrorCode: 4 });
    expect(notice).toMatchObject({ kind: 'media_error', offerProxy: true });
    expect(notice.kind === 'media_error' && notice.message).toContain('não suportado pelo WebView2');
  });

  it('HEVC que só carrega o áudio não vira tela preta silenciosa', () => {
    const notice = resolveClipNotice({ ...base, state: ready({ codec: 'hevc', likelyPlayable: null }), noVideoTrack: true });
    expect(notice).toMatchObject({ kind: 'no_video_track', offerProxy: true });
  });

  it('o proxy falhando não oferece gerar proxy de novo em loop', () => {
    const notice = resolveClipNotice({ ...base, state: ready({ status: 'proxy', proxyStatus: 'ready' }), hasMediaError: true, mediaErrorCode: 3 });
    expect(notice).toMatchObject({ kind: 'media_error', offerProxy: false });
  });
});

describe('regras de tempo do player', () => {
  it('clique na faixa vira segundos, limitado às bordas', () => {
    expect(stripPositionToSeconds(150, 100, 200, 60)).toBe(15);
    expect(stripPositionToSeconds(50, 100, 200, 60)).toBe(0);
    expect(stripPositionToSeconds(900, 100, 200, 60)).toBe(60);
    expect(stripPositionToSeconds(150, 100, 0, 60)).toBeNull();
    expect(stripPositionToSeconds(150, 100, 200, 0)).toBeNull();
  });

  it('busca respeita a duração e rejeita valor inválido', () => {
    expect(clampSeek(500, 120)).toBe(120);
    expect(clampSeek(-3, 120)).toBe(0);
    expect(clampSeek(Number.NaN, 120)).toBe(0);
    expect(clampSeek(40, null)).toBe(40);
  });

  it('formata o tempo', () => {
    expect(formatPlayerTime(65.4)).toBe('01:05');
    expect(formatPlayerTime(3725)).toBe('1:02:05');
    expect(formatPlayerTime(null)).toBe('--:--');
  });

  it('proxyIndicator só informa o que o host disse', () => {
    expect(proxyIndicator(source(), false)).toBeNull();
    expect(proxyIndicator(source({ proxyStatus: 'ready' }), false)).toBe('Proxy disponível');
    expect(proxyIndicator(source({ proxyStatus: 'failed' }), false)).toContain('falhou');
  });

  it('classifica o tipo da mídia sem depender de maiúsculas', () => {
    expect(isImageAsset({ kind: 'Image' })).toBe(true);
    expect(isAudioAsset({ kind: 'audio' })).toBe(true);
    expect(isAudioAsset({ kind: 'Video' })).toBe(false);
  });
});
