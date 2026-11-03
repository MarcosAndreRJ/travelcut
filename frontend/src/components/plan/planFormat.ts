/** Rótulos PT-BR para valores do Plano editorial (F06). Valores desconhecidos aparecem sem tradução, nunca inventados. */

const LABELS_ROLE: Record<string, string> = {
  abertura: 'Abertura',
  desenvolvimento: 'Desenvolvimento',
  encerramento: 'Encerramento'
};

export const roleLabel = (role: string): string => LABELS_ROLE[role] ?? role;

const LABELS_CONFIDENCE: Record<string, string> = {
  low: 'Baixa',
  medium: 'Média',
  high: 'Alta'
};

/** Confiança qualitativa (nunca percentual). */
export const confidenceLabel = (band: string | null | undefined): string =>
  (band && LABELS_CONFIDENCE[band]) || 'Não informada';

export const confidenceClass = (band: string | null | undefined): string => {
  switch (band) {
    case 'high':
      return 'text-emerald-300 bg-emerald-950/60 border-emerald-800/50';
    case 'medium':
      return 'text-amber-300 bg-amber-950/60 border-amber-800/50';
    case 'low':
      return 'text-rose-300 bg-rose-950/60 border-rose-800/50';
    default:
      return 'text-slate-300 bg-slate-800 border-slate-700';
  }
};

const LABELS_SEVERITY: Record<string, string> = { error: 'Erro', warning: 'Aviso' };
export const severityLabel = (severity: string): string => LABELS_SEVERITY[severity] ?? severity;

const LABELS_PENDING: Record<string, string> = {
  suggestion_pending: 'Sugestão pendente',
  music_suggestion: 'Sugestão de música',
  zoom_suggestion: 'Zoom sugerido',
  speed_suggestion: 'Velocidade sugerida',
  media_excluded: 'Mídia excluída',
  residual_segment: 'Trecho residual curto'
};
export const pendingKindLabel = (kind: string): string => LABELS_PENDING[kind] ?? kind;

const LABELS_TRANSITION: Record<string, string> = {
  cut: 'Corte',
  dissolve: 'Dissolve',
  fade: 'Fade'
};
export const transitionLabel = (kind: string): string => LABELS_TRANSITION[kind] ?? kind;

const LABELS_TRACK_KIND: Record<string, string> = {
  video: 'Vídeo',
  dialogue: 'Diálogo',
  music: 'Música'
};
export const trackKindLabel = (kind: string): string => LABELS_TRACK_KIND[kind] ?? kind;

/** Rótulos do perfil editorial (EditorialProfileDto). */
export const profileValueLabel = (field: string, value: string | boolean): string => {
  if (typeof value === 'boolean') return value ? 'Sim' : 'Não';
  const table: Record<string, Record<string, string>> = {
    silencePolicy: { conservative: 'Conservadora', balanced: 'Equilibrada', aggressive: 'Agressiva' },
    pace: { calm: 'Calmo', normal: 'Normal', fast: 'Rápido' },
    jokePreservation: { strict: 'Estrita', normal: 'Normal' },
    zoomStrength: { off: 'Desligado', subtle: 'Sutil', medium: 'Médio' },
    speedBroll: { off: 'Desligado', '2x': '2×', '4x': '4×', auto: 'Automático' },
    musicPolicy: { suggest_only: 'Somente sugerir', off: 'Desligada' }
  };
  return table[field]?.[value] ?? value;
};

export const PROFILE_FIELD_LABEL: Record<string, string> = {
  silencePolicy: 'Silêncios',
  pace: 'Ritmo',
  jokePreservation: 'Preservar piadas',
  zoomStrength: 'Intensidade do zoom',
  speedBroll: 'Aceleração de B-roll',
  musicPolicy: 'Música',
  reviewAmbiguous: 'Revisar ambíguos',
  autoApplyTrivialCuts: 'Aplicar cortes triviais'
};

/** Nome do arquivo a partir de caminho relativo (aceita \\ e /). */
export const basename = (path: string): string => path.split(/[\\/]/).filter(Boolean).pop() ?? path;

/** Data/hora curta em PT-BR a partir de ISO UTC; inválida vira '—'. */
export const formatDateTime = (iso: string | null | undefined): string => {
  if (!iso) return '—';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '—';
  return date.toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' });
};
