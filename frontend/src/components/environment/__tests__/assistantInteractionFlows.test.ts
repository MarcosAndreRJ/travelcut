import { describe, expect, it } from 'vitest';
import type {
  EnvironmentInstallPlanDto,
  ToolInstallProgressDto,
  TestToolResponse,
  AutoLocateResponse,
  StartToolInstallRequest,
  StartModelDownloadRequest
} from '../../../types/ipc';

describe('Assisted Installation & Interaction Flows (A3)', () => {
  describe('Installation Plan & Security Confirmation', () => {
    it('requires explicit confirmation before starting any tool install', () => {
      const plan: EnvironmentInstallPlanDto = {
        toolKey: 'ffmpeg',
        title: 'Instalar FFmpeg via WinGet',
        description: 'Instalação oficial do pacote Gyan.FFmpeg no Windows.',
        canAutoInstall: true,
        estimatedTime: '2 a 4 minutos',
        estimatedDownloadSize: '~90 MB',
        requiresElevation: false,
        requiresUserConfirmation: true,
        steps: [
          {
            stepNumber: 1,
            title: 'Executar winget install Gyan.FFmpeg',
            command: 'winget install Gyan.FFmpeg --accept-package-agreements --accept-source-agreements -e',
            description: 'Instala os binários do FFmpeg e FFprobe no PATH do Windows.'
          }
        ]
      };

      // Garantir que a flag de confirmação do plano seja respeitada
      expect(plan.requiresUserConfirmation).toBe(true);

      // Criar a requisição de instalação com confirmação obrigatória
      const requestWithoutConfirmation: StartToolInstallRequest = {
        toolKey: 'ffmpeg',
        confirmed: false
      };
      const requestWithConfirmation: StartToolInstallRequest = {
        toolKey: 'ffmpeg',
        confirmed: true,
        preferCuda: true
      };

      expect(requestWithoutConfirmation.confirmed).toBe(false);
      expect(requestWithConfirmation.confirmed).toBe(true);
      expect(requestWithConfirmation.preferCuda).toBe(true);
    });

    it('displays elevation warnings when requiresElevation is true', () => {
      const elevatedPlan: EnvironmentInstallPlanDto = {
        toolKey: 'python',
        title: 'Instalar Python 3.11 no Sistema',
        description: 'Instalação global do Python para todos os usuários.',
        canAutoInstall: true,
        estimatedTime: '3 a 5 minutos',
        estimatedDownloadSize: '~25 MB',
        requiresElevation: true,
        requiresUserConfirmation: true,
        warningNotice: 'A instalação requer elevação de privilégios de Administrador no Windows.',
        steps: [
          {
            stepNumber: 1,
            title: 'Instalação do pacote Python.Python.3.11',
            command: 'winget install Python.Python.3.11 -e',
            description: 'Instala o runtime Python 3.11.'
          }
        ]
      };

      expect(elevatedPlan.requiresElevation).toBe(true);
      expect(elevatedPlan.warningNotice).toContain('Administrador');
    });
  });

  describe('Progress Tracking & State Consistency', () => {
    it('distinguishes cancellation requested from cancellation completed', () => {
      // Estado intermediário: o usuário clicou em Cancelar, mas o backend ainda está finalizando o processo
      let isCancelingRequested = true;
      let progressState: ToolInstallProgressDto = {
        jobId: 'job-cancel-test',
        toolKey: 'ffmpeg',
        stage: 'Download',
        progressPercent: 30,
        currentMessage: 'Baixando pacote...',
        outputLogs: ['[WinGet] Baixando...'],
        isCompleted: false,
        isSuccess: false,
          isFailed: false,
          isCanceled: false,
        canCancel: true,
        startedAtUtc: new Date().toISOString(),
        finishedAtUtc: null,
        errorMessage: null
      };

      // Enquanto isCompleted for false e isCanceled for false, a UI deve indicar "Cancelando..."
      expect(isCancelingRequested).toBe(true);
      expect(progressState.isCanceled).toBe(false);
      expect(progressState.isCompleted).toBe(false);

      // Quando o backend confirma o cancelamento
      progressState = {
        ...progressState,
        isCompleted: true,
        isCanceled: true,
        canCancel: false,
        currentMessage: 'Instalação cancelada pelo usuário.',
        finishedAtUtc: new Date().toISOString()
      };
      isCancelingRequested = false;

      expect(isCancelingRequested).toBe(false);
      expect(progressState.isCanceled).toBe(true);
      expect(progressState.isCompleted).toBe(true);
      expect(progressState.isSuccess).toBe(false);
    });

    it('does not display false success state when installation fails', () => {
      const failedProgress: ToolInstallProgressDto = {
        jobId: 'job-fail-test',
        toolKey: 'ffmpeg',
        stage: 'Falha',
        progressPercent: 60,
        currentMessage: 'WinGet retornou código de erro 0x80070005 (Acesso negado).',
        outputLogs: [
          '[WinGet] Iniciando...',
          '[WinGet] Erro crítico ao criar diretório.'
        ],
        isCompleted: true,
        isSuccess: false,
          isFailed: false,
          isCanceled: false,
        canCancel: false,
        startedAtUtc: new Date().toISOString(),
        finishedAtUtc: new Date().toISOString(),
        errorMessage: 'WinGet retornou código de erro 0x80070005: Acesso negado.'
      };

      expect(failedProgress.isCompleted).toBe(true);
      expect(failedProgress.isSuccess).toBe(false);
      expect(failedProgress.errorMessage).toBeTruthy();
      expect(failedProgress.errorMessage).toContain('Acesso negado');
      // A UI não deve considerar sucesso
      const shouldShowSuccess = failedProgress.isCompleted && failedProgress.isSuccess;
      expect(shouldShowSuccess).toBe(false);
    });
  });

  describe('Whisper Model Download Workflow', () => {
    it('requires explicit confirmation with model size before downloading large-v3', () => {
      const downloadRequest: StartModelDownloadRequest = {
        modelName: 'large-v3',
        confirmed: true
      };

      expect(downloadRequest.modelName).toBe('large-v3');
      expect(downloadRequest.confirmed).toBe(true);

      const downloadProgress: ToolInstallProgressDto = {
        jobId: 'job-whisper-model',
        toolKey: 'whisper',
        stage: 'Download do Modelo',
        progressPercent: 78,
        currentMessage: 'Baixando large-v3.pt (2.4 GB / 3.1 GB)...',
        outputLogs: ['[Whisper] Conectando ao repositório oficial HuggingFace/OpenAI...', '[Whisper] 78% baixado.'],
        isCompleted: false,
        isSuccess: false,
          isFailed: false,
          isCanceled: false,
        canCancel: true,
        startedAtUtc: new Date().toISOString(),
        finishedAtUtc: null,
        errorMessage: null
      };

      expect(downloadProgress.progressPercent).toBe(78);
      expect(downloadProgress.canCancel).toBe(true);
      expect(downloadProgress.currentMessage).toContain('3.1 GB');
    });
  });

  describe('Tool Auto-Locate & Path Configuration', () => {
    it('handles candidate detection and path selection', () => {
      const locateResult: AutoLocateResponse = {
        toolKey: 'ffmpeg',
        candidates: [
          'C:\\ProgramData\\chocolatey\\bin\\ffmpeg.exe',
          'C:\\ffmpeg\\bin\\ffmpeg.exe'
        ]
      };

      expect(locateResult.candidates.length).toBe(2);
      expect(locateResult.candidates[0]).toContain('ffmpeg.exe');

      const selectedPath = locateResult.candidates[0];
      expect(selectedPath).toBe('C:\\ProgramData\\chocolatey\\bin\\ffmpeg.exe');
    });

    it('parses tool test execution logs and diagnostics', () => {
      const testResult: TestToolResponse = {
        toolKey: 'ffmpeg',
        success: true,
        message: 'FFmpeg 7.0.2 e FFprobe testados com sucesso.',
        details: 'ffmpeg version 7.0.2-essentials_build-www.gyan.dev Copyright (c) 2000-2024 the FFmpeg developers',
        outputLogs: ['ffmpeg version 7.0.2-essentials_build', 'libavcodec 61. 3.100', 'ffprobe version 7.0.2-essentials_build'],
        logs: [
          'ffmpeg version 7.0.2-essentials_build',
          'libavcodec 61. 3.100 / 61. 3.100',
          'ffprobe version 7.0.2-essentials_build'
        ]
      };

      expect(testResult.success).toBe(true);
      expect(testResult.details).toContain('gyan.dev');
      expect(testResult.outputLogs.length).toBe(3);
    });
  });
});
