// Gerenciador unificado de notificações visuais e sonoras (F13.5 Checkpoint D).
import { playSuccessChime, playFailureChime } from './soundEffects';

export interface AppNotification {
  id: string;
  title: string;
  message: string;
  type: 'success' | 'error' | 'info';
  timestamp: number;
}

type NotificationListener = (notification: AppNotification) => void;
const listeners = new Set<NotificationListener>();

export function subscribeNotifications(listener: NotificationListener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function dispatchNotification(notification: AppNotification): void {
  listeners.forEach((listener) => {
    try {
      listener(notification);
    } catch (err) {
      console.error('Erro ao notificar listener in-app:', err);
    }
  });

  // Notificação desktop nativa quando a janela/aba estiver em segundo plano
  if (typeof window !== 'undefined' && 'Notification' in window) {
    if (Notification.permission === 'granted' && document.visibilityState === 'hidden') {
      try {
        new Notification(notification.title, {
          body: notification.message,
          icon: '/favicon.ico'
        });
      } catch {
        // Ignora erros de permissão de notificação nativa
      }
    }
  }
}

/** Notifica conclusão bem-sucedida do download de modelo de IA. */
export function notifyModelDownloadSuccess(modelName: string): void {
  playSuccessChime();
  dispatchNotification({
    id: `model-dl-${Date.now()}`,
    title: 'Download de modelo concluído',
    message: `O modelo ${modelName} foi baixado e validado com sucesso. Whisper pronto para transcrição.`,
    type: 'success',
    timestamp: Date.now()
  });
}

/** Notifica conclusão bem-sucedida da fila de análise. */
export function notifyPipelineSuccess(processedCount: number): void {
  playSuccessChime();
  const countText = processedCount > 0 ? `${processedCount} vídeo${processedCount > 1 ? 's' : ''} processado${processedCount > 1 ? 's' : ''}` : 'Processamento concluído';
  dispatchNotification({
    id: `pipeline-success-${Date.now()}`,
    title: 'Análise concluída com sucesso',
    message: `${countText}. Transcrições e classificação de cenas disponíveis para revisão.`,
    type: 'success',
    timestamp: Date.now()
  });
}

/** Notifica falha na análise que demanda atenção do usuário. */
export function notifyPipelineFailure(reason: string): void {
  playFailureChime();
  dispatchNotification({
    id: `pipeline-failed-${Date.now()}`,
    title: 'Falha na análise',
    message: reason || 'Ocorreu um erro durante o processamento da fila.',
    type: 'error',
    timestamp: Date.now()
  });
}

/** Notificação geral. */
export function notifyGeneral(title: string, message: string, type: 'success' | 'error' | 'info' = 'info'): void {
  if (type === 'success') {
    playSuccessChime();
  } else if (type === 'error') {
    playFailureChime();
  }
  dispatchNotification({
    id: `general-${Date.now()}`,
    title,
    message,
    type,
    timestamp: Date.now()
  });
}
