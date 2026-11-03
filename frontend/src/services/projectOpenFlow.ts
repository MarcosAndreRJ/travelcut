import { IPC_TIMEOUT_CODE, PROJECT_IPC_TIMEOUT_MS } from './ipcBridge';

// Regras de tela para abrir/criar projeto (sem regra de negócio: o host decide e devolve o código).

/** Código do host quando a pasta existe mas faltam pastas canônicas e autoCreateFolders=false. */
export const MISSING_FOLDERS_CODE = 'INVALID_FOUR_FOLDERS';

const codeOf = (err: unknown): string | null => {
  const raw = (err as { code?: unknown } | null)?.code;
  return typeof raw === 'string' && raw.length > 0 ? raw : null;
};

/** true quando o host pede confirmação para criar pastas canônicas ausentes. */
export function isMissingFoldersError(err: unknown): boolean {
  return codeOf(err) === MISSING_FOLDERS_CODE;
}

export function isIpcTimeoutError(err: unknown): boolean {
  return codeOf(err) === IPC_TIMEOUT_CODE;
}

const PROJECT_TIMEOUT_SECONDS = Math.round(PROJECT_IPC_TIMEOUT_MS / 1000);

/**
 * Mensagem para o usuário. Timeout não é falha confirmada: o host pode ainda estar processando,
 * então a mensagem pede verificação antes de repetir a operação.
 */
export function projectOperationErrorMessage(err: unknown, kind: 'open' | 'create'): string {
  if (isIpcTimeoutError(err)) {
    return kind === 'open'
      ? `O host ainda está processando a abertura do projeto (limite de ${PROJECT_TIMEOUT_SECONDS} s excedido). ` +
          'A operação pode continuar no computador host. Aguarde e tente de novo; se persistir, confira a pasta do projeto.'
      : `A criação ainda está em processamento no host (limite de ${PROJECT_TIMEOUT_SECONDS} s excedido). ` +
          'Antes de tentar de novo, confira se a pasta do projeto já existe para não duplicar.';
  }
  const message = err instanceof Error ? err.message : typeof err === 'string' ? err : '';
  if (message.trim().length > 0) return message;
  return kind === 'open'
    ? 'Falha ao abrir projeto. Verifique o caminho e a estrutura de pastas.'
    : 'Falha ao criar o projeto. Verifique as permissões da pasta.';
}

/** Texto de confirmação exibido antes de criar pastas ausentes (somente pastas; nenhum arquivo é alterado). */
export const CREATE_MISSING_FOLDERS_QUESTION =
  'Criar as pastas canônicas ausentes neste projeto? Somente pastas vazias serão criadas; arquivos existentes não são alterados.';
