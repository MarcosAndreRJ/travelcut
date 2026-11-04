import { describe, expect, it, vi } from 'vitest';
import { ErrorBoundary } from '../ErrorBoundary';
import { ipc } from '../../services/ipcBridge';

describe('ErrorBoundary unit tests', () => {
  it('getDerivedStateFromError gera estado com hasError true e captura o objeto de erro', () => {
    const error = new Error('Falha simulada no componente');
    const state = ErrorBoundary.getDerivedStateFromError(error);

    expect(state.hasError).toBe(true);
    expect(state.error).toBe(error);
  });

  it('componentDidCatch envia log de erro do cliente via ipc', () => {
    const spy = vi.spyOn(ipc, 'send').mockResolvedValue(undefined as never);
    const error = new Error('Erro de renderização');
    const errorInfo = { componentStack: '    at DummyComponent\n    at App' };

    const instance = new ErrorBoundary({ children: null });
    instance.componentDidCatch(error, errorInfo);

    expect(spy).toHaveBeenCalledWith('system.logClientError', {
      message: 'Erro de renderização',
      stack: error.stack,
      componentStack: errorInfo.componentStack
    });

    spy.mockRestore();
  });

  it('handleReset reseta o estado interno de erro e invoca callback onReset se fornecido', () => {
    const onResetMock = vi.fn();
    const instance = new ErrorBoundary({ children: null, onReset: onResetMock });

    instance.state = {
      hasError: true,
      error: new Error('Erro anterior'),
      errorInfo: null,
      showDetails: true,
      copied: false
    };

    const setStateSpy = vi.spyOn(instance, 'setState').mockImplementation((updater) => {
      if (typeof updater === 'object') {
        instance.state = { ...instance.state, ...updater };
      }
    });

    (instance as any).handleReset();

    expect(setStateSpy).toHaveBeenCalledWith({
      hasError: false,
      error: null,
      errorInfo: null,
      showDetails: false,
      copied: false
    });
    expect(onResetMock).toHaveBeenCalledTimes(1);

    setStateSpy.mockRestore();
  });
});
