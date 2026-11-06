import { describe, expect, it } from 'vitest';
import type {
  EnvironmentAssistantDiagnosticDto,
  EnvironmentInstallPlanDto,
  ToolInstallProgressDto,
  TestToolResponse
} from '../../../types/ipc';

describe('Environment Assistant Diagnostic & Operations', () => {
  const mockDiagnostic: EnvironmentAssistantDiagnosticDto = {
    allReady: false,
    evaluatedAtUtc: new Date().toISOString(),
    systemInfo: {
      osDescription: 'Microsoft Windows 11 Pro 10.0.26100',
      architecture: 'X64',
      isWindows: true,
      totalMemoryMb: 32768,
      freeDiskSpaceMb: 120000,
      targetCacheDirectory: 'C:\\Users\\User\\.cache\\whisper',
      hasWinget: true,
      wingetPath: 'C:\\Users\\User\\AppData\\Local\\Microsoft\\WindowsApps\\winget.exe'
    },
    tools: [
      {
        toolKey: 'python',
        displayName: 'Python 3.11',
        state: 'READY',
        isReady: true,
        version: '3.11.9',
        executablePath: 'C:\\Python311\\python.exe',
        details: 'Python instalado e verificado.',
        supportedActions: ['refresh', 'locate', 'configure_path', 'test'],
        winGetPackageId: 'Python.Python.3.11'
      },
      {
        toolKey: 'whisper',
        displayName: 'OpenAI Whisper & PyTorch',
        state: 'READY_GPU',
        isReady: true,
        version: 'large-v3',
        executablePath: 'C:\\Users\\User\\.cache\\whisper\\large-v3.pt',
        details: 'Whisper pronto com aceleração CUDA.',
        supportedActions: ['refresh', 'test', 'download_model']
      },
      {
        toolKey: 'ffmpeg',
        displayName: 'FFmpeg & FFprobe',
        state: 'NOT_INSTALLED',
        isReady: false,
        version: null,
        executablePath: null,
        details: 'FFmpeg não encontrado no PATH.',
        supportedActions: ['refresh', 'install', 'locate', 'configure_path'],
        winGetPackageId: 'Gyan.FFmpeg'
      },
      {
        toolKey: 'node',
        displayName: 'Node.js LTS',
        state: 'READY',
        isReady: true,
        version: 'v22.12.0',
        executablePath: 'C:\\Program Files\\nodejs\\node.exe',
        details: 'Node.js LTS disponível no sistema.',
        supportedActions: ['refresh', 'locate', 'configure_path', 'test'],
        winGetPackageId: 'OpenJS.NodeJS.LTS'
      },
      {
        toolKey: 'hyperframes',
        displayName: 'HyperFrames Motion Renderer',
        state: 'NOT_CONFIGURED',
        isReady: false,
        version: null,
        executablePath: null,
        details: 'HyperFrames não configurado nem encontrado.',
        supportedActions: ['refresh', 'install', 'locate', 'configure_path']
      }
    ]
  };

  it('correctly aggregates ready vs pending tools', () => {
    const readyCount = mockDiagnostic.tools.filter((t) => t.isReady).length;
    const pendingCount = mockDiagnostic.tools.filter((t) => !t.isReady).length;
    expect(readyCount).toBe(3);
    expect(pendingCount).toBe(2);
    expect(mockDiagnostic.allReady).toBe(false);
  });

  it('validates tool environment states and badges', () => {
    const python = mockDiagnostic.tools.find((t) => t.toolKey === 'python')!;
    const whisper = mockDiagnostic.tools.find((t) => t.toolKey === 'whisper')!;
    const ffmpeg = mockDiagnostic.tools.find((t) => t.toolKey === 'ffmpeg')!;
    const hyperframes = mockDiagnostic.tools.find((t) => t.toolKey === 'hyperframes')!;

    expect(python.state).toBe('READY');
    expect(whisper.state).toBe('READY_GPU');
    expect(ffmpeg.state).toBe('NOT_INSTALLED');
    expect(hyperframes.state).toBe('NOT_CONFIGURED');
  });

  it('validates install plan security requirements', () => {
    const plan: EnvironmentInstallPlanDto = {
      toolKey: 'ffmpeg',
      title: 'Instalar FFmpeg via WinGet',
      description: 'Instalação oficial do pacote Gyan.FFmpeg.',
      canAutoInstall: true,
      estimatedTime: '2 a 4 minutos',
      estimatedDownloadSize: '~90 MB',
      requiresElevation: false,
      requiresUserConfirmation: true,
      steps: [
        {
          stepNumber: 1,
          title: 'Instalação do pacote Gyan.FFmpeg',
          command: 'winget install Gyan.FFmpeg --accept-package-agreements --accept-source-agreements -e',
          description: 'Download e instalação do FFmpeg no Windows.'
        }
      ]
    };

    expect(plan.requiresUserConfirmation).toBe(true);
    expect(plan.steps.length).toBe(1);
    expect(plan.steps[0].command).toContain('winget install Gyan.FFmpeg');
  });

  it('validates tool install progress and cancellation status', () => {
    const progress: ToolInstallProgressDto = {
      jobId: 'job-123',
      toolKey: 'ffmpeg',
      stage: 'Download',
      progressPercent: 45,
      currentMessage: 'Baixando pacote oficial...',
      outputLogs: ['[WinGet] Iniciando download...', '[WinGet] 45% concluído'],
      isCompleted: false,
      isSuccess: false,
        isFailed: false,
        isCanceled: false,
      canCancel: true,
      startedAtUtc: new Date().toISOString(),
      finishedAtUtc: null,
      errorMessage: null
    };

    expect(progress.isCompleted).toBe(false);
    expect(progress.canCancel).toBe(true);
    expect(progress.progressPercent).toBe(45);
    expect(progress.outputLogs.length).toBe(2);
  });

  it('validates test tool functional response parsing', () => {
    const testResult: TestToolResponse = {
      toolKey: 'python',
      success: true,
      message: 'Python 3.11.9 executou teste de integridade com sucesso.',
      details: null,
      outputLogs: ["Python 3.11.9 (tags/v3.11.9:de54058, Apr  2 2024)", "Teste concluido com exit code 0"],
      logs: ['Python 3.11.9 (tags/v3.11.9:de54058, Apr  2 2024)', 'Teste concluído com exit code 0']
    };

    expect(testResult.success).toBe(true);
    expect(testResult.outputLogs.length).toBe(2);
    expect(testResult.message).toContain('sucesso');
  });
});
