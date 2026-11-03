import { useCallback, useEffect, useRef, useState } from 'react';
import { ipc } from '../../services/ipcBridge';
import type {
  EditorApplicationsResponse,
  EditorApplyPreview,
  EditorApplyResult
} from '../../types/ipc';
import {
  buildApplyRequest,
  canSendApply,
  describeEditorError,
  newIdempotencyKey,
  sortApplicationsNewestFirst,
  type EditorErrorInfo
} from './editorModel';

export type HistoryStatus = 'idle' | 'loading' | 'ready' | 'error';

export interface EditorApplyController {
  preview: EditorApplyPreview | null;
  preparing: boolean;
  prepareError: EditorErrorInfo | null;
  confirmed: boolean;
  idempotencyKey: string | null;
  sending: boolean;
  result: EditorApplyResult | null;
  sendError: EditorErrorInfo | null;
  history: EditorApplyResult[];
  historyStatus: HistoryStatus;
  historyError: EditorErrorInfo | null;
  prepare: () => Promise<void>;
  send: () => Promise<void>;
  setConfirmed: (value: boolean) => void;
  reloadHistory: () => Promise<void>;
}

/**
 * Estado da tela de envio ao editor. Respostas de uma preparação ou projeto anterior são descartadas.
 * A chave de idempotência é gerada uma vez por preparação e reaproveitada nas novas tentativas do envio.
 * Nenhum "sucesso" é mostrado sem resposta do host: o resultado só entra quando editor.apply responde.
 */
export function useEditorApply(projectId: string | null): EditorApplyController {
  const [preview, setPreview] = useState<EditorApplyPreview | null>(null);
  const [preparing, setPreparing] = useState(false);
  const [prepareError, setPrepareError] = useState<EditorErrorInfo | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [idempotencyKey, setIdempotencyKey] = useState<string | null>(null);
  const [sending, setSending] = useState(false);
  const [result, setResult] = useState<EditorApplyResult | null>(null);
  const [sendError, setSendError] = useState<EditorErrorInfo | null>(null);
  const [history, setHistory] = useState<EditorApplyResult[]>([]);
  const [historyStatus, setHistoryStatus] = useState<HistoryStatus>('idle');
  const [historyError, setHistoryError] = useState<EditorErrorInfo | null>(null);

  // Projeto atual e "sessão" de preparação: trocas de projeto ou novas preparações invalidam respostas em voo.
  const activeProjectRef = useRef<string | null>(projectId);
  const sessionRef = useRef(0);

  const loadHistory = useCallback(async (): Promise<void> => {
    const pid = activeProjectRef.current;
    if (!pid) return;
    setHistoryStatus('loading');
    setHistoryError(null);
    try {
      const res = await ipc.send<EditorApplicationsResponse>('editor.applications', { projectId: pid });
      if (activeProjectRef.current !== pid) return;
      setHistory(sortApplicationsNewestFirst(res?.applications ?? []));
      setHistoryStatus('ready');
    } catch (err) {
      if (activeProjectRef.current !== pid) return;
      setHistoryError(describeEditorError(err));
      setHistoryStatus('error');
    }
  }, []);

  useEffect(() => {
    activeProjectRef.current = projectId;
    sessionRef.current += 1;
    setPreview(null);
    setPreparing(false);
    setPrepareError(null);
    setConfirmed(false);
    setIdempotencyKey(null);
    setSending(false);
    setResult(null);
    setSendError(null);
    setHistory([]);
    setHistoryError(null);
    if (!projectId) {
      setHistoryStatus('idle');
      return;
    }
    void loadHistory();
  }, [projectId, loadHistory]);

  const prepare = useCallback(async (): Promise<void> => {
    const pid = activeProjectRef.current;
    if (!pid || sending) return;
    sessionRef.current += 1;
    const session = sessionRef.current;
    setPreparing(true);
    setPrepareError(null);
    setPreview(null);
    setResult(null);
    setSendError(null);
    setConfirmed(false);
    setIdempotencyKey(null);
    try {
      // revision null = última revisão do plano (a mesma que a Tela 03 mostra por padrão).
      const p = await ipc.send<EditorApplyPreview>('editor.prepareApply', { projectId: pid, revision: null });
      if (session !== sessionRef.current) return;
      setPreview(p);
      setIdempotencyKey(newIdempotencyKey());
    } catch (err) {
      if (session !== sessionRef.current) return;
      setPrepareError(describeEditorError(err));
    } finally {
      if (session === sessionRef.current) setPreparing(false);
    }
  }, [sending]);

  const send = useCallback(async (): Promise<void> => {
    const pid = activeProjectRef.current;
    if (!pid || !preview || !idempotencyKey) return;
    if (!canSendApply(preview, confirmed, sending)) return;
    const session = sessionRef.current;
    setSending(true);
    setSendError(null);
    try {
      const res = await ipc.send<EditorApplyResult>(
        'editor.apply',
        buildApplyRequest(pid, preview, confirmed, idempotencyKey)
      );
      if (session !== sessionRef.current) return;
      setResult(res);
      void loadHistory();
    } catch (err) {
      if (session !== sessionRef.current) return;
      setSendError(describeEditorError(err));
    } finally {
      if (session === sessionRef.current) setSending(false);
    }
  }, [preview, confirmed, idempotencyKey, sending, loadHistory]);

  return {
    preview,
    preparing,
    prepareError,
    confirmed,
    idempotencyKey,
    sending,
    result,
    sendError,
    history,
    historyStatus,
    historyError,
    prepare,
    send,
    setConfirmed,
    reloadHistory: loadHistory
  };
}
