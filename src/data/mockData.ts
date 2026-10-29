import {
  MediaClip,
  StoryboardScene,
  AudioTrackConfig,
  MusicTrack,
  ExportPreset,
  ExportJob,
  SystemConfig,
  TimelineClip
} from '../types';

export const INITIAL_CLIPS: MediaClip[] = [
  {
    id: 'clip-1',
    title: 'Pôr do Sol em Shinjuku Neon',
    filename: 'SHINJUKU_SUNSET_4K60_001.MOV',
    location: 'Tóquio, Japão (Shinjuku)',
    date: '14/10/2024',
    time: '17:42',
    durationSec: 38,
    durationFormatted: '00:38',
    resolution: '4K 60fps',
    fps: 60,
    camera: 'Sony FX3 + 24-70mm GM II',
    category: 'Paisagem',
    thumbnail: 'https://images.unsplash.com/photo-1503899036084-c55cdd92da26?auto=format&fit=crop&w=720&q=80',
    quality: 'Excelente',
    stability: 'Gimbal Suave',
    facesCount: 3,
    aiScore: 98,
    aiAnalysisSummary: 'Iluminação crepuscular espetacular, contraste de neons urbanos e movimento suave de câmera em 60fps.',
    waveformPoints: [20, 35, 45, 60, 50, 70, 85, 90, 75, 60, 55, 65, 80, 95, 70, 50, 40, 30, 45, 60],
    suggestedCuts: [
      {
        id: 'cut-1-1',
        startSec: 4.2,
        endSec: 16.8,
        timecodeStart: '00:04:12',
        timecodeEnd: '00:16:48',
        durationFormatted: '12.6s',
        score: 98,
        reason: 'Pico dourado do pôr do sol alinhado com o acendimento dos neons da avenida principal.',
        tags: ['Pôr do Sol', 'Golden Hour', 'Neons', 'Cinema'],
        accepted: true,
      },
      {
        id: 'cut-1-2',
        startSec: 22.0,
        endSec: 32.5,
        timecodeStart: '00:22:00',
        timecodeEnd: '00:32:30',
        durationFormatted: '10.5s',
        score: 91,
        reason: 'Close-up na reação de admiração das pessoas atravessando o cruzamento.',
        tags: ['Pessoas', 'Street Life', 'Profundidade'],
        accepted: false,
      }
    ],
    transcription: [
      {
        id: 'tr-1',
        start: 5.1,
        end: 9.3,
        timecodeStart: '00:05:06',
        timecodeEnd: '00:09:18',
        speaker: 'Lucas (Vlog)',
        text: 'Olhem as cores desse céu agora caindo sobre Shinjuku... surreal!',
        highlightWords: ['cores', 'céu', 'Shinjuku', 'surreal'],
        emotion: 'entusiasmo'
      },
      {
        id: 'tr-2',
        start: 11.0,
        end: 15.8,
        timecodeStart: '00:11:00',
        timecodeEnd: '00:15:48',
        speaker: 'Lucas (Vlog)',
        text: 'A sensação de caminhar por aqui nessa hora é indescritível.',
        highlightWords: ['sensação', 'caminhar', 'indescritível'],
        emotion: 'contemplativo'
      }
    ]
  },
  {
    id: 'clip-2',
    title: 'Templo Senso-ji Incenso & Sino',
    filename: 'ASAKUSA_SENSOJI_4K24_014.MOV',
    location: 'Asakusa, Tóquio',
    date: '15/10/2024',
    time: '09:15',
    durationSec: 45,
    durationFormatted: '00:45',
    resolution: '4K 24fps',
    fps: 24,
    camera: 'Sony FX3 + 35mm F1.4 GM',
    category: 'Monumentos',
    thumbnail: 'https://images.unsplash.com/photo-1545569341-9eb8b30979d9?auto=format&fit=crop&w=720&q=80',
    quality: 'Excelente',
    stability: 'Tripé Fixo',
    facesCount: 6,
    aiScore: 94,
    aiAnalysisSummary: 'Fumaça de incenso com raios de luz matinais (god rays). Áudio ambiente riquíssimo com o sino sagrado.',
    waveformPoints: [30, 40, 50, 45, 60, 75, 65, 55, 45, 80, 85, 90, 70, 60, 50, 40, 35, 30, 25, 20],
    suggestedCuts: [
      {
        id: 'cut-2-1',
        startSec: 2.0,
        endSec: 14.5,
        timecodeStart: '00:02:00',
        timecodeEnd: '00:14:30',
        durationFormatted: '12.5s',
        score: 95,
        reason: 'Raios solares atravessando o incensário com peregrinos jogando fumaça.',
        tags: ['Espiritual', 'Slow Motion', 'God Rays'],
        accepted: true,
      }
    ],
    transcription: [
      {
        id: 'tr-3',
        start: 2.5,
        end: 7.2,
        timecodeStart: '00:02:12',
        timecodeEnd: '00:07:05',
        speaker: 'Lucas (Vlog)',
        text: 'A tradição diz que essa fumaça traz cura e clareza mental.',
        highlightWords: ['fumaça', 'cura', 'clareza mental'],
        emotion: 'contemplativo'
      }
    ]
  },
  {
    id: 'clip-3',
    title: 'Street Food em Dotonbori Takoyaki',
    filename: 'OSAKA_DOTONBORI_FOOD_1080P120.MP4',
    location: 'Osaka, Dotonbori',
    date: '17/10/2024',
    time: '20:10',
    durationSec: 52,
    durationFormatted: '00:52',
    resolution: '1080p 120fps',
    fps: 120,
    camera: 'Sony A7S III + 50mm F1.2 GM',
    category: 'Gastronomia',
    thumbnail: 'https://images.unsplash.com/photo-1590559899731-a382839e5549?auto=format&fit=crop&w=720&q=80',
    quality: 'Excelente',
    stability: 'Gimbal Suave',
    facesCount: 2,
    aiScore: 97,
    aiAnalysisSummary: 'Cozimento de Takoyaki em alta velocidade com chapa incandescente. Som crocante estalando.',
    waveformPoints: [60, 70, 85, 90, 80, 75, 85, 95, 100, 85, 75, 80, 90, 85, 70, 65, 80, 85, 75, 60],
    suggestedCuts: [
      {
        id: 'cut-3-1',
        startSec: 8.5,
        endSec: 18.0,
        timecodeStart: '00:08:30',
        timecodeEnd: '00:18:00',
        durationFormatted: '9.5s',
        score: 97,
        reason: 'Giro rítmico dos palitos virando os takoyakis com katsuobushi dançando no vapor.',
        tags: ['Gastronomia', 'Vapor', 'Satisfatório'],
        accepted: true,
      }
    ],
    transcription: [
      {
        id: 'tr-4',
        start: 9.0,
        end: 14.1,
        timecodeStart: '00:09:00',
        timecodeEnd: '00:14:06',
        speaker: 'Lucas (Vlog)',
        text: 'Gente, olhem a agilidade desse mestre! Isso aqui é pura arte.',
        highlightWords: ['agilidade', 'mestre', 'pura arte'],
        emotion: 'divertido'
      }
    ]
  },
  {
    id: 'clip-4',
    title: 'Trem-Bala Shinkansen Vista do Monte Fuji',
    filename: 'SHINKANSEN_FUJI_VIEW_4K60_029.MOV',
    location: 'Shizuoka, Linha Tokaido',
    date: '18/10/2024',
    time: '11:28',
    durationSec: 64,
    durationFormatted: '01:04',
    resolution: '4K 60fps',
    fps: 60,
    camera: 'Sony FX3 + 70-200mm GM II',
    category: 'Transporte',
    thumbnail: 'https://images.unsplash.com/photo-1490806843957-31f4c9a91c65?auto=format&fit=crop&w=720&q=80',
    quality: 'Excelente',
    stability: 'Tripé Fixo',
    facesCount: 1,
    aiScore: 99,
    aiAnalysisSummary: 'Monte Fuji perfeitamente límpido sem nuvens, com velocidade do Shinkansen a 285 km/h.',
    waveformPoints: [40, 45, 50, 55, 60, 65, 70, 75, 70, 65, 60, 55, 50, 45, 50, 55, 60, 65, 70, 60],
    suggestedCuts: [
      {
        id: 'cut-4-1',
        startSec: 15.0,
        endSec: 30.0,
        timecodeStart: '00:15:00',
        timecodeEnd: '00:30:00',
        durationFormatted: '15.0s',
        score: 99,
        reason: 'Composição perfeita da montanha com plantações de chá verde em primeiro plano.',
        tags: ['Monte Fuji', 'Épico', 'Cinemático'],
        accepted: true,
      }
    ],
    transcription: [
      {
        id: 'tr-5',
        start: 16.0,
        end: 21.4,
        timecodeStart: '00:16:00',
        timecodeEnd: '00:21:24',
        speaker: 'Lucas (Vlog)',
        text: 'Não acredito... o Fuji apareceu completamente limpo na janela!',
        highlightWords: ['Fuji', 'limpo', 'janela'],
        emotion: 'entusiasmo'
      }
    ]
  },
  {
    id: 'clip-5',
    title: 'Floresta de Bambu de Arashiyama Vento',
    filename: 'KYOTO_ARASHIYAMA_BAMBOO_4K24_03.MOV',
    location: 'Kyoto, Japão',
    date: '19/10/2024',
    time: '07:30',
    durationSec: 42,
    durationFormatted: '00:42',
    resolution: '4K 24fps',
    fps: 24,
    camera: 'Sony FX3 + 16-35mm GM II',
    category: 'Paisagem',
    thumbnail: 'https://images.unsplash.com/photo-1503899036084-c55cdd92da26?auto=format&fit=crop&w=720&q=80',
    quality: 'Boa',
    stability: 'Gimbal Suave',
    facesCount: 0,
    aiScore: 92,
    aiAnalysisSummary: 'Caminho vazio pela manhã com farfalhar sutil dos bambus gigantes ao vento.',
    waveformPoints: [15, 20, 25, 30, 25, 35, 40, 35, 30, 25, 30, 35, 40, 45, 35, 30, 25, 20, 15, 10],
    suggestedCuts: [
      {
        id: 'cut-5-1',
        startSec: 5.0,
        endSec: 16.0,
        timecodeStart: '00:05:00',
        timecodeEnd: '00:16:00',
        durationFormatted: '11.0s',
        score: 93,
        reason: 'Movimento contínuo de push-in no túnel verde sem nenhuma pessoa no enquadramento.',
        tags: ['Zen', 'Natureza', 'Imersivo'],
        accepted: false,
      }
    ],
    transcription: [
      {
        id: 'tr-6',
        start: 6.0,
        end: 10.5,
        timecodeStart: '00:06:00',
        timecodeEnd: '00:10:30',
        speaker: 'Lucas (Vlog)',
        text: 'Acordar às seis da manhã valeu a pena só pra ouvir esse silêncio.',
        highlightWords: ['seis da manhã', 'silêncio'],
        emotion: 'contemplativo'
      }
    ]
  },
  {
    id: 'clip-6',
    title: 'Portal Torii Vermelho em Fushimi Inari',
    filename: 'KYOTO_FUSHIMI_INARI_4K60_050.MOV',
    location: 'Kyoto, Fushimi Inari',
    date: '19/10/2024',
    time: '14:50',
    durationSec: 50,
    durationFormatted: '00:50',
    resolution: '4K 60fps',
    fps: 60,
    camera: 'Sony FX3 + 24-70mm GM II',
    category: 'Monumentos',
    thumbnail: 'https://images.unsplash.com/photo-1493976040374-85c8e12f0c0e?auto=format&fit=crop&w=720&q=80',
    quality: 'Excelente',
    stability: 'Estabilizado IA',
    facesCount: 4,
    aiScore: 96,
    aiAnalysisSummary: 'Caminhada rápida em túnel de toriis vermelhos criando efeito estroboscópico hipnotizante de luz e sombra.',
    waveformPoints: [35, 45, 60, 50, 70, 65, 80, 85, 90, 75, 65, 70, 80, 85, 70, 60, 50, 45, 40, 35],
    suggestedCuts: [
      {
        id: 'cut-6-1',
        startSec: 10.0,
        endSec: 22.0,
        timecodeStart: '00:10:00',
        timecodeEnd: '00:22:00',
        durationFormatted: '12.0s',
        score: 96,
        reason: 'Efeito rítmico dos pilares vermelhos sincronizável perfeitamente com batida musical.',
        tags: ['Ritmo', 'Cores Vibrantes', 'Cultura'],
        accepted: true,
      }
    ],
    transcription: [
      {
        id: 'tr-7',
        start: 11.2,
        end: 16.0,
        timecodeStart: '00:11:12',
        timecodeEnd: '00:16:00',
        speaker: 'Lucas (Vlog)',
        text: 'São mais de 10 mil portais subindo a montanha inteira.',
        highlightWords: ['10 mil portais', 'montanha'],
        emotion: 'curiosidade'
      }
    ]
  }
];

export const INITIAL_STORYBOARD: StoryboardScene[] = [
  {
    id: 'scene-1',
    order: 1,
    title: 'Abertura & Choque Cultural',
    location: 'Tóquio - Shinjuku Neon',
    vibe: 'Enérgica & Urbana',
    targetDurationSec: 12,
    actualDurationSec: 12.6,
    assignedClipIds: ['clip-1'],
    musicMood: 'Synthwave / Lofi Beat pulsante',
    soundEffect: 'Murmúrio da multidão + neons',
    narrativeGoal: 'Capturar a atenção imediata nos primeiros 5 segundos com visuais ultra-vibrantes de Tóquio à noite.'
  },
  {
    id: 'scene-2',
    order: 2,
    title: 'Espiritualidade & Tradição Milenar',
    location: 'Asakusa - Templo Senso-ji',
    vibe: 'Contemplativa & Mística',
    targetDurationSec: 14,
    actualDurationSec: 12.5,
    assignedClipIds: ['clip-2'],
    musicMood: 'Flauta Shakuhachi + Taiko suave',
    soundEffect: 'Sino budista ressonante + braseiro',
    narrativeGoal: 'Contraste intencional entre o futurismo e as raízes espirituais do Japão.'
  },
  {
    id: 'scene-3',
    order: 3,
    title: 'Velocidade e o Monte Sagrado',
    location: 'Trem-Bala Shinkansen Tokaido',
    vibe: 'Épica & Deslumbrante',
    targetDurationSec: 15,
    actualDurationSec: 15.0,
    assignedClipIds: ['clip-4'],
    musicMood: 'Crescendo orquestral cinematográfico',
    soundEffect: 'Zumbido aerodinâmico do trem a 300km/h',
    narrativeGoal: 'O ápice da jornada: visual perfeito e raro do Monte Fuji da janela.'
  },
  {
    id: 'scene-4',
    order: 4,
    title: 'Explosão de Sabores em Osaka',
    location: 'Dotonbori Street Food',
    vibe: 'Divertida & Dinâmica',
    targetDurationSec: 10,
    actualDurationSec: 9.5,
    assignedClipIds: ['clip-3'],
    musicMood: 'Funk japonês moderno / Bateria ritmada',
    soundEffect: 'Chiado da chapa quente + risos',
    narrativeGoal: 'Aproximação calorosa com o povo japonês através da culinária de rua.'
  },
  {
    id: 'scene-5',
    order: 5,
    title: 'Caminho dos Mil Portais & Desfecho',
    location: 'Kyoto - Fushimi Inari',
    vibe: 'Memorável & Inspiradora',
    targetDurationSec: 12,
    actualDurationSec: 12.0,
    assignedClipIds: ['clip-6', 'clip-5'],
    musicMood: 'Harmonia acolhedora com piano e cordas',
    soundEffect: 'Passos na terra + vento suave',
    narrativeGoal: 'Conclusão emocional da viagem, deixando a mensagem de transformação pessoal.'
  }
];

export const INITIAL_AUDIO_TRACKS: AudioTrackConfig[] = [
  {
    id: 'track-speech',
    name: 'Voz / Narração Principal (A1)',
    type: 'speech',
    volumeDb: 0,
    isMuted: false,
    isSolo: false,
    speechEnhance: 85,
    noiseReductionDb: -18,
    autoDucking: true,
    duckingAmountDb: -14,
    eqLow: -2.5,
    eqMid: +3.0,
    eqHigh: +1.5,
  },
  {
    id: 'track-ambient',
    name: 'Som Ambiente da Câmera (A2)',
    type: 'ambient',
    volumeDb: -6,
    isMuted: false,
    isSolo: false,
    speechEnhance: 0,
    noiseReductionDb: -8,
    autoDucking: true,
    duckingAmountDb: -12,
    eqLow: 0,
    eqMid: -1.0,
    eqHigh: 0,
  },
  {
    id: 'track-music',
    name: 'Trilha Sonora & Beats (A3)',
    type: 'music',
    volumeDb: -10,
    isMuted: false,
    isSolo: false,
    speechEnhance: 0,
    noiseReductionDb: 0,
    autoDucking: true,
    duckingAmountDb: -16,
    eqLow: +1.0,
    eqMid: 0,
    eqHigh: +0.5,
  },
  {
    id: 'track-foley',
    name: 'Efeitos Sonoros & Foley (A4)',
    type: 'foley',
    volumeDb: -4,
    isMuted: false,
    isSolo: false,
    speechEnhance: 0,
    noiseReductionDb: 0,
    autoDucking: false,
    duckingAmountDb: 0,
    eqLow: +2.0,
    eqMid: 0,
    eqHigh: +2.0,
  }
];

export const MOCK_MUSIC_LIBRARY: MusicTrack[] = [
  {
    id: 'music-1',
    title: 'Tokyo Sunset Horizon (Cinematic Beat)',
    artist: 'TravelCut Originals / Kyoto SoundLab',
    bpm: 118,
    genre: 'Cinematic Lo-Fi / Synth',
    durationFormatted: '03:24',
    durationSec: 204,
    mood: 'Nostálgico, Urbano, Hipnótico',
    beatTimesSec: [1.2, 2.4, 3.6, 4.8, 6.0, 7.2, 8.4, 9.6, 10.8, 12.0, 13.2, 14.4, 15.6, 16.8, 18.0, 19.2, 20.4]
  },
  {
    id: 'music-2',
    title: 'Fuji Majestic Journey',
    artist: 'Aoyama String Ensemble',
    bpm: 88,
    genre: 'Orchestral Epic',
    durationFormatted: '02:50',
    durationSec: 170,
    mood: 'Grandioso, Inspirador, Amplo',
    beatTimesSec: [2.0, 4.0, 6.0, 8.0, 10.0, 12.0, 14.0, 16.0, 18.0, 20.0, 22.0]
  },
  {
    id: 'music-3',
    title: 'Dotonbori Night Grooves',
    artist: 'Osaka Beatmakers Club',
    bpm: 126,
    genre: 'Future Funk / Street Beats',
    durationFormatted: '02:15',
    durationSec: 135,
    mood: 'Enérgico, Festivo, Ágil',
    beatTimesSec: [0.95, 1.9, 2.85, 3.8, 4.75, 5.7, 6.65, 7.6, 8.55, 9.5]
  }
];

export const EXPORT_PRESETS: ExportPreset[] = [
  {
    id: 'preset-yt-4k',
    name: 'YouTube 4K Ultra HD',
    platform: 'YouTube',
    ratio: '16:9',
    resolution: '3840 x 2160',
    width: 3840,
    height: 2160,
    fps: 60,
    codec: 'H.265 / HEVC',
    bitrateMbps: 65,
    audioBitrateKbps: 320,
    estimatedSizeMb: 485,
    iconName: 'youtube'
  },
  {
    id: 'preset-reels-916',
    name: 'Instagram Reels & Stories',
    platform: 'Instagram',
    ratio: '9:16',
    resolution: '1080 x 1920 (Vertical)',
    width: 1080,
    height: 1920,
    fps: 60,
    codec: 'H.264',
    bitrateMbps: 25,
    audioBitrateKbps: 256,
    estimatedSizeMb: 142,
    iconName: 'instagram'
  },
  {
    id: 'preset-tiktok-916',
    name: 'TikTok Viral High-Bitrate',
    platform: 'TikTok',
    ratio: '9:16',
    resolution: '1080 x 1920 (Vertical)',
    width: 1080,
    height: 1920,
    fps: 60,
    codec: 'H.264',
    bitrateMbps: 28,
    audioBitrateKbps: 320,
    estimatedSizeMb: 168,
    iconName: 'video'
  },
  {
    id: 'preset-prores-master',
    name: 'Master ProRes 422 HQ (Arquivo Final)',
    platform: 'Master ProRes',
    ratio: '16:9',
    resolution: '3840 x 2160',
    width: 3840,
    height: 2160,
    fps: 60,
    codec: 'ProRes 422 HQ',
    bitrateMbps: 440,
    audioBitrateKbps: 1536,
    estimatedSizeMb: 3450,
    iconName: 'film'
  }
];

export const INITIAL_EXPORT_QUEUE: ExportJob[] = [
  {
    id: 'job-1',
    presetName: 'YouTube 4K Ultra HD (3840x2160 60fps)',
    resolution: '4K UHD (16:9)',
    codec: 'H.265 / HEVC (NVENC)',
    progress: 74,
    status: 'rendering',
    etaSec: 18,
    outputFilename: 'Japao_Viagem_2024_Master_YouTube4K.mp4',
    filesizeMb: 485
  },
  {
    id: 'job-2',
    presetName: 'Instagram Reels (1080x1920 Vertical 9:16)',
    resolution: '1080x1920 (9:16)',
    codec: 'H.264 High Profile',
    progress: 100,
    status: 'completed',
    etaSec: 0,
    outputFilename: 'Reels_Japao_Melhores_Momentos.mp4',
    filesizeMb: 142
  },
  {
    id: 'job-3',
    presetName: 'TikTok Viral HD (9:16 60fps)',
    resolution: '1080x1920 (9:16)',
    codec: 'H.264 Main',
    progress: 0,
    status: 'queued',
    etaSec: 45,
    outputFilename: 'TikTok_Dotonbori_StreetFood.mp4',
    filesizeMb: 168
  }
];

export const INITIAL_SYSTEM_CONFIG: SystemConfig = {
  aiWhisperModel: 'Whisper Large-v3 (Local GPU)',
  aiVisionEngine: 'YOLOv8 + CLIP ViT-L/14',
  hardwareAccel: 'NVIDIA CUDA (RTX 4090 / TensorRT)',
  renderScratchDisk: 'D:\\TravelCut_Scratch\\Cache',
  proxyGeneration: 'Automático ProRes Proxy',
  xmlNleExport: 'DaVinci Resolve (.xml / .edl)',
  autoDuckingLevel: -16,
  elevenLabsApiKeySet: true,
  autoDetectSilences: true,
};

export const MOCK_MEDIA_CLIPS = INITIAL_CLIPS;

export const INITIAL_TIMELINE_CLIPS: TimelineClip[] = [
  {
    id: 'tl-1',
    mediaClipId: 'clip-1',
    title: 'Abertura Shinjuku Golden Hour',
    trackIndex: 0,
    timelineStartSec: 0,
    sourceInSec: 4.2,
    sourceOutSec: 16.8,
    durationSec: 12.6,
    colorGradeLut: 'Tokyo Warm Sunset',
    transition: 'Cross Dissolve 0.5s'
  },
  {
    id: 'tl-2',
    mediaClipId: 'clip-2',
    title: 'Templo Senso-ji Braseiro',
    trackIndex: 0,
    timelineStartSec: 12.6,
    sourceInSec: 3.5,
    sourceOutSec: 16.0,
    durationSec: 12.5,
    colorGradeLut: 'Kyoto Golden Autumn',
    transition: 'Whip Pan 0.3s'
  },
  {
    id: 'tl-3',
    mediaClipId: 'clip-4',
    title: 'Trem-Bala Vista Monte Fuji',
    trackIndex: 0,
    timelineStartSec: 25.1,
    sourceInSec: 8.0,
    sourceOutSec: 23.0,
    durationSec: 15.0,
    colorGradeLut: 'Fuji Moody Mist',
    transition: 'Cross Dissolve 0.5s'
  },
  {
    id: 'tl-4',
    mediaClipId: 'clip-3',
    title: 'Osaka Takoyaki na Chapa',
    trackIndex: 0,
    timelineStartSec: 40.1,
    sourceInSec: 2.0,
    sourceOutSec: 11.5,
    durationSec: 9.5,
    colorGradeLut: 'Dotonbori Neon Punch',
    transition: 'Hard Cut'
  },
  {
    id: 'tl-5',
    mediaClipId: 'clip-6',
    title: 'Fushimi Inari Torii Run',
    trackIndex: 0,
    timelineStartSec: 49.6,
    sourceInSec: 10.0,
    sourceOutSec: 22.0,
    durationSec: 12.0,
    colorGradeLut: 'Tokyo Warm Sunset',
    transition: 'Dip to Black 0.8s'
  }
];
