import { describe, expect, it } from 'vitest';
import { IpcError } from '../ipcBridge';
import {
  CREATE_MISSING_FOLDERS_QUESTION,
  MISSING_FOLDERS_CODE,
  isIpcTimeoutError,
  isMissingFoldersError,
  projectOperationErrorMessage
} from '../projectOpenFlow';

describe('isMissingFoldersError', () => {
  it('reconhece o código INVALID_FOUR_FOLDERS do host', () => {
    const err = new IpcError('A pasta obrigatória 3-Trabalho não foi encontrada.', MISSING_FOLDERS_CODE);
    expect(isMissingFoldersError(err)).toBe(true);
  });

  it('não confunde com DIRECTORY_NOT_FOUND nem erros sem código', () => {
    expect(isMissingFoldersError(new IpcError('x', 'DIRECTORY_NOT_FOUND'))).toBe(false);
    expect(isMissingFoldersError(new Error('INVALID_FOUR_FOLDERS'))).toBe(false);
    expect(isMissingFoldersError(null)).toBe(false);
    expect(isMissingFoldersError('INVALID_FOUR_FOLDERS')).toBe(false);
  });
});

describe('projectOperationErrorMessage', () => {
  it('timeout de abertura não é tratado como falha confirmada', () => {
    const err = new IpcError('Timeout ao aguardar resposta IPC para project.open', 'IPC_TIMEOUT');
    expect(isIpcTimeoutError(err)).toBe(true);
    const msg = projectOperationErrorMessage(err, 'open');
    expect(msg).toContain('ainda está processando');
    expect(msg).toContain('120 s');
    expect(msg).not.toMatch(/falhou|erro/i);
  });

  it('timeout de criação orienta a conferir a pasta antes de repetir', () => {
    const err = new IpcError('Timeout', 'IPC_TIMEOUT');
    const msg = projectOperationErrorMessage(err, 'create');
    expect(msg).toContain('ainda está em processamento');
    expect(msg).toContain('não duplicar');
  });

  it('usa a mensagem real do host quando não há timeout', () => {
    const err = new IpcError('O diretório informado não foi encontrado.', 'DIRECTORY_NOT_FOUND');
    expect(projectOperationErrorMessage(err, 'open')).toBe('O diretório informado não foi encontrado.');
  });

  it('nunca devolve mensagem vazia', () => {
    expect(projectOperationErrorMessage(new Error(''), 'open')).toContain('Falha ao abrir projeto');
    expect(projectOperationErrorMessage(undefined, 'create')).toContain('Falha ao criar o projeto');
  });
});

describe('pergunta de confirmação', () => {
  it('deixa claro que só pastas são criadas e arquivos não são alterados', () => {
    expect(CREATE_MISSING_FOLDERS_QUESTION).toContain('Somente pastas');
    expect(CREATE_MISSING_FOLDERS_QUESTION).toContain('não são alterados');
  });
});
