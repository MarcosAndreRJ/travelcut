using TravelCut.Application.Audio;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Tooling;

public sealed partial class EnvironmentAssistantService
{
    private sealed class ToolInstallJobContext
    {
        public Guid JobId { get; }
        public string ToolKey { get; }
        public CancellationTokenSource Cts { get; }
        public string Stage { get; set; } = "Iniciando";
        public int? ProgressPercent { get; set; }
        public string CurrentMessage { get; set; } = "Preparando instalação...";
        public List<string> OutputLogs { get; } = new();
        public bool CanCancel { get; set; } = true;
        public bool IsCompleted { get; set; }
        public bool IsSuccess { get; set; }
        public bool IsFailed { get; set; }
        public string? ErrorMessage { get; set; }
        public object Lock { get; } = new();

        public ToolInstallJobContext(Guid jobId, string toolKey, CancellationTokenSource cts)
        {
            JobId = jobId;
            ToolKey = toolKey;
            Cts = cts;
        }

        public void AddLog(string line)
        {
            lock (Lock)
            {
                OutputLogs.Add(line);
            }
        }

        public ToolInstallProgressDto ToDto()
        {
            lock (Lock)
            {
                return new ToolInstallProgressDto(
                    JobId: JobId,
                    ToolKey: ToolKey,
                    Stage: Stage,
                    ProgressPercent: ProgressPercent,
                    CurrentMessage: CurrentMessage,
                    OutputLogs: OutputLogs.ToList(),
                    CanCancel: CanCancel,
                    IsCompleted: IsCompleted,
                    IsSuccess: IsSuccess,
                    IsFailed: IsFailed,
                    ErrorMessage: ErrorMessage
                );
            }
        }
    }

    private sealed class ModelDownloadJobContext
    {
        public Guid JobId { get; }
        public string ModelName { get; }
        public CancellationTokenSource Cts { get; }
        public string Stage { get; set; } = "Iniciando";
        public int? ProgressPercent { get; set; }
        public string CurrentMessage { get; set; } = "Preparando download...";
        public List<string> OutputLogs { get; } = new();
        public bool CanCancel { get; set; } = true;
        public bool IsCompleted { get; set; }
        public bool IsSuccess { get; set; }
        public bool IsFailed { get; set; }
        public bool IsCanceled { get; set; }
        public long? BytesDownloaded { get; set; }
        public long? TotalBytes { get; set; }
        public string? ErrorMessage { get; set; }
        public object Lock { get; } = new();

        public ModelDownloadJobContext(Guid jobId, string modelName, CancellationTokenSource cts)
        {
            JobId = jobId;
            ModelName = modelName;
            Cts = cts;
        }

        public void AddLog(string line)
        {
            lock (Lock)
            {
                OutputLogs.Add(line);
            }
        }

        public ToolInstallProgressDto ToDto()
        {
            lock (Lock)
            {
                return new ToolInstallProgressDto(
                    JobId: JobId,
                    ToolKey: "whisper",
                    Stage: Stage,
                    ProgressPercent: ProgressPercent,
                    CurrentMessage: CurrentMessage,
                    OutputLogs: OutputLogs.ToList(),
                    CanCancel: CanCancel,
                    IsCompleted: IsCompleted,
                    IsSuccess: IsSuccess,
                    IsFailed: IsFailed,
                    ErrorMessage: ErrorMessage,
                    BytesDownloaded: BytesDownloaded,
                    TotalBytes: TotalBytes,
                    IsCanceled: IsCanceled
                );
            }
        }

        /// <summary>Atualização vinda do downloader. Depois do estado terminal é ignorada (não rebaixa 100% para 99%).</summary>
        public void ApplyDownloadProgress(DownloadProgressUpdate update)
        {
            lock (Lock)
            {
                if (IsCompleted) return;

                BytesDownloaded = update.BytesRead;
                TotalBytes = update.TotalBytes;
                ProgressPercent = update.Percent.HasValue ? (int)Math.Clamp(update.Percent.Value, 0, 99) : null;

                var mbRead = update.BytesRead / (1024.0 * 1024.0);
                if (update.TotalBytes is long total && total > 0)
                {
                    if (update.BytesRead >= total)
                    {
                        // O SHA-256 é confirmado ao fim da transferência, antes de mover o arquivo para o destino.
                        Stage = "Verificando SHA-256";
                        CurrentMessage = $"Transferência concluída ({mbRead:F1} MB). Confirmando o hash SHA-256 oficial...";
                        return;
                    }
                    CurrentMessage = $"Baixando {ModelName}: {mbRead:F1} MB de {total / (1024.0 * 1024.0):F1} MB ({ProgressPercent}%)...";
                }
                else
                {
                    CurrentMessage = $"Baixando {ModelName}: {mbRead:F1} MB (tamanho total não informado pelo servidor)...";
                }
            }
        }

        public void CompleteSuccess(string message)
        {
            lock (Lock)
            {
                Stage = "Concluído";
                ProgressPercent = 100;
                CurrentMessage = message;
                CanCancel = false;
                IsSuccess = true;
                IsCompleted = true;
            }
        }

        public void CompleteCanceled()
        {
            lock (Lock)
            {
                Stage = "Cancelado";
                CurrentMessage = "Download cancelado pelo usuário.";
                CanCancel = false;
                IsCanceled = true;
                IsCompleted = true;
            }
        }

        public void CompleteFailed(string errorMessage)
        {
            lock (Lock)
            {
                Stage = "Erro";
                ErrorMessage = errorMessage;
                CurrentMessage = $"Falha no download: {errorMessage}";
                CanCancel = false;
                IsFailed = true;
                IsCompleted = true;
            }
        }
    }

    public async Task<EnvironmentInstallPlanDto> GetInstallPlanAsync(string toolKey, CancellationToken ct = default)
    {
        var key = toolKey?.Trim().ToLowerInvariant() ?? string.Empty;
        if (key is not ("python" or "node" or "whisper" or "hyperframes" or "ffmpeg"))
        {
            throw new ArgumentException($"Plano de instalação não suportado para '{key}'.", nameof(key));
        }

        // Limpeza de tokens de planos expirados
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _planAuthorizations)
        {
            if (kvp.Value.ExpiresAtUtc < now) _planAuthorizations.TryRemove(kvp.Key, out _);
        }

        var approvalToken = Guid.NewGuid().ToString("N");
        _planAuthorizations[approvalToken] = new PlanAuthorization(key, now.AddMinutes(15));

        var plan = await CreatePlanDtoInternalAsync(key, ct).ConfigureAwait(false);
        return plan with { ApprovalToken = approvalToken };
    }

    private async Task<EnvironmentInstallPlanDto> CreatePlanDtoInternalAsync(string key, CancellationToken ct)
    {
        var winget = ToolPathResolver.FindWinGet();

        EnvironmentInstallPlanDto plan;
        switch (key)
        {
            case "python":
                if (winget != null)
                {
                    plan = new EnvironmentInstallPlanDto(
                        ToolKey: "python",
                        Title: "Instalar Python 3.11 via WinGet",
                        Description: "Instalação oficial e isolada do Python 3.11 no Windows usando o WinGet (Package ID: Python.Python.3.11).",
                        CanAutoInstall: true,
                        EstimatedTime: "1 a 3 minutos",
                        EstimatedDownloadSize: "~35 MB",
                        RequiresElevation: false,
                        RequiresUserConfirmation: true,
                        Steps: new List<InstallStepDto>
                        {
                            new(1, "Instalação do pacote Python", "winget install --id Python.Python.3.11 -e --silent --accept-package-agreements --accept-source-agreements", "Baixa e instala o interpretador no perfil do usuário sem interação."),
                            new(2, "Verificação de integridade", "python --version", "Garante que o executável está no PATH ou nos diretórios padrão do usuário.")
                        }
                    );
                }
                else
                {
                    plan = new EnvironmentInstallPlanDto(
                        ToolKey: "python",
                        Title: "Instalação Manual do Python",
                        Description: "WinGet não localizado. Instale o Python 3.10+ via instalador oficial ou Microsoft Store.",
                        CanAutoInstall: false,
                        EstimatedTime: "5 a 10 minutos",
                        EstimatedDownloadSize: "~35 MB",
                        RequiresElevation: true,
                        RequiresUserConfirmation: true,
                        Steps: new List<InstallStepDto>
                        {
                            new(1, "Download", "Acessar https://www.python.org/downloads/", "Baixar instalador oficial para Windows."),
                            new(2, "Instalar", "python-3.11.x-amd64.exe", "Marcar 'Add python.exe to PATH' durante a instalação."),
                            new(3, "Configurar", "Configurar caminho", "Informar o caminho manual nas configurações do TravelCut Studio caso necessário.")
                        },
                        Warning: "Instalação manual requer interação humana e privilégios de administrador."
                    );
                }
                break;

            case "node":
                if (winget != null)
                {
                    plan = new EnvironmentInstallPlanDto(
                        ToolKey: "node",
                        Title: "Instalar Node.js LTS via WinGet",
                        Description: "Instalação do Node.js LTS no Windows usando o WinGet (Package ID: OpenJS.NodeJS.LTS). Requer versão 22+ para HyperFrames.",
                        CanAutoInstall: true,
                        EstimatedTime: "1 a 2 minutos",
                        EstimatedDownloadSize: "~30 MB",
                        RequiresElevation: false,
                        RequiresUserConfirmation: true,
                        Steps: new List<InstallStepDto>
                        {
                            new(1, "Instalação do Node.js", "winget install --id OpenJS.NodeJS.LTS -e --silent --accept-package-agreements --accept-source-agreements", "Executa a instalação automatizada do Node.js LTS."),
                            new(2, "Verificação de versão", "node --version", "Confirma a disponibilidade no PATH ou Program Files.")
                        }
                    );
                }
                else
                {
                    plan = new EnvironmentInstallPlanDto(
                        ToolKey: "node",
                        Title: "Instalação Manual do Node.js",
                        Description: "WinGet não localizado. Instale o Node.js 22+ via instalador oficial.",
                        CanAutoInstall: false,
                        EstimatedTime: "3 a 5 minutos",
                        EstimatedDownloadSize: "~30 MB",
                        RequiresElevation: true,
                        RequiresUserConfirmation: true,
                        Steps: new List<InstallStepDto>
                        {
                            new(1, "Download", "Acessar https://nodejs.org/", "Baixar o instalador LTS recomendado para Windows (Node 22+)."),
                            new(2, "Instalar", "node-v22.x-x64.msi", "Concluir assistente padrão."),
                            new(3, "Configurar", "Configurar caminho", "Informar o caminho do node.exe caso não esteja no PATH.")
                        }
                    );
                }
                break;

            case "whisper":
                var pyDiag = await DiagnosePythonAsync(ct).ConfigureAwait(false);
                var pyAvailable = pyDiag.IsReady && pyDiag.ExecutablePath != null;
                var venvPath = ToolPathResolver.GetPersistentPythonVenvDirectory();

                plan = new EnvironmentInstallPlanDto(
                    ToolKey: "whisper",
                    Title: "Criar Ambiente Virtual e Instalar OpenAI Whisper",
                    Description: $"Cria um ambiente virtual Python isolado persistente em {venvPath} e instala PyTorch e openai-whisper.",
                    CanAutoInstall: pyAvailable,
                    EstimatedTime: "3 a 8 minutos",
                    EstimatedDownloadSize: "~800 MB (PyTorch + openai-whisper + dependências)",
                    RequiresElevation: false,
                    RequiresUserConfirmation: true,
                    Steps: new List<InstallStepDto>
                    {
                        new(1, "Criação do ambiente virtual persistente", $"python -m venv {venvPath}", "Cria ambiente isolado fora das worktrees temporárias."),
                        new(2, "Atualização do pip", "python.exe -m pip install --upgrade pip", "Prepara o gerenciador de pacotes local."),
                        new(3, "Instalação do PyTorch", "pip install torch --index-url https://download.pytorch.org/whl/cpu", "Instala a biblioteca de inferência otimizada."),
                        new(4, "Instalação do openai-whisper", "pip install openai-whisper", "Instala o pacote oficial de reconhecimento de fala."),
                        new(5, "Verificação do worker", "python workers/whisper_worker.py", "Executa comando probe e confirma comunicação NDJSON.")
                    },
                    Warning: pyAvailable ? null : "É necessário instalar o Python 3.8+ antes de configurar o Whisper."
                );
                break;

            case "hyperframes":
                var nodeDiag = await DiagnoseNodeAsync(ct).ConfigureAwait(false);
                var nodeMajor = 0;
                if (nodeDiag.Version != null)
                {
                    var matchNode = Regex.Match(nodeDiag.Version, @"(?:v)?(d+)");
                    if (matchNode.Success) int.TryParse(matchNode.Groups[1].Value, out nodeMajor);
                }
                var nodeAvailable = nodeDiag.IsReady && nodeMajor >= 22;
                var hfDir = ToolPathResolver.GetPersistentHyperFramesDirectory();

                plan = new EnvironmentInstallPlanDto(
                    ToolKey: "hyperframes",
                    Title: "Instalar HyperFrames Motion Renderer via npm",
                    Description: $"Instala o pacote hyperframes@0.9.1 (versão compatível testada) no diretório persistente {hfDir}. Requer Node.js 22+.",
                    CanAutoInstall: nodeAvailable,
                    EstimatedTime: "1 a 2 minutos",
                    EstimatedDownloadSize: "~40 MB",
                    RequiresElevation: false,
                    RequiresUserConfirmation: true,
                    Steps: new List<InstallStepDto>
                    {
                        new(1, "Validação de requisitos", "node --version", "Verifica Node.js 22 ou superior."),
                        new(2, "Instalação isolada via npm", "npm install --prefix ... hyperframes@0.9.1", $"Instala o renderizador em {hfDir}."),
                        new(3, "Localização do CLI", "Verifica node_modules/hyperframes/dist/cli.js", "Detecta o ponto de entrada do executável."),
                        new(4, "Teste de execução real", "node cli.js --version", "Garante resposta válida do CLI sem depender de mocks.")
                    },
                    Warning: nodeAvailable ? null : "É necessário Node.js versão 22 ou superior para instalar o HyperFrames."
                );
                break;

            case "ffmpeg":
                plan = new EnvironmentInstallPlanDto(
                    ToolKey: "ffmpeg",
                    Title: "Instalação do FFmpeg",
                    Description: "O FFmpeg e o FFprobe fornecem suporte a decodificação, transcodificação, extração de áudio e miniaturas.",
                    CanAutoInstall: winget != null,
                    EstimatedTime: "1 a 2 minutos",
                    EstimatedDownloadSize: "~80 MB",
                    RequiresElevation: false,
                    RequiresUserConfirmation: true,
                    Steps: new List<InstallStepDto>
                    {
                        new(1, "Instalação via WinGet", "winget install --id Gyan.FFmpeg -e", "Instala a distribuição oficial FFmpeg para Windows."),
                        new(2, "Registro nas configurações", "Atualiza caminho no TravelCut Studio", "Permite que os processos internos usem o ffmpeg.")
                    }
                );
                break;

            default:
                throw new ArgumentException($"Plano de instalação não suportado para '{key}'.", nameof(key));
        }

        return plan;
    }

    public async Task<Guid> StartInstallAsync(StartToolInstallRequest request, CancellationToken ct = default)
    {
        if (!request.Confirmed)
        {
            throw new InvalidOperationException("Instalação requer confirmação explícita.");
        }

        var key = request.ToolKey?.Trim().ToLowerInvariant() ?? string.Empty;
        if (key is not ("python" or "node" or "whisper" or "hyperframes" or "ffmpeg"))
        {
            throw new ArgumentException($"Ferramenta desconhecida '{request.ToolKey}'.", nameof(request.ToolKey));
        }

        // Validação do token de aprovação do plano (Section 3: autorização de instalações vinculada ao plano)
        if (string.IsNullOrWhiteSpace(request.ApprovalToken) || !_planAuthorizations.TryRemove(request.ApprovalToken, out var auth))
        {
            throw new InvalidOperationException("Instalação não autorizada: token de aprovação do plano ausente, inválido ou expirado.");
        }

        if (auth.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("O plano de instalação aprovado expirou. Gere um novo plano.");
        }

        if (!string.Equals(auth.ToolKey, key, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Token de aprovação incompatível: o plano foi gerado para '{auth.ToolKey}', mas a instalação solicitada é para '{key}'.");
        }

        // Controle de concorrência: impedir múltiplos jobs ativos para a mesma ferramenta
        bool hasActiveJob = _installJobs.Values.Any(j => !j.IsCompleted && !j.IsFailed && string.Equals(j.ToolKey, key, StringComparison.OrdinalIgnoreCase));
        if (hasActiveJob)
        {
            throw new InvalidOperationException($"Já existe uma instalação em andamento para a ferramenta '{key}'.");
        }

        if (!string.IsNullOrWhiteSpace(request.CustomInstallPath))
        {
            var custom = request.CustomInstallPath.Trim();
            if (!Path.IsPathRooted(custom) || custom.Contains(".."))
            {
                throw new ArgumentException("O caminho de instalação personalizado deve ser absoluto e sem sequências de travessia ('..').");
            }
        }

        // Recupera o plano autoritativo para confirmação nativa do host
        var plan = await CreatePlanDtoInternalAsync(key, ct).ConfigureAwait(false);

        // Confirmação nativa no host WPF: impede disparos não autorizados por scripts JS no WebView2
        var nativeReq = new NativeInstallationRequest(
            ToolKey: key,
            Title: plan.Title,
            Description: plan.Description,
            Steps: plan.Steps.Select(s => $"{s.StepNumber}. {s.Title} ({s.Command})").ToList(),
            TargetDirectory: request.CustomInstallPath?.Trim() ?? null,
            RequiresElevation: plan.RequiresElevation
        );

        var isAuthorized = await _nativeAuthorizer.RequestInstallationAuthorizationAsync(nativeReq, ct).ConfigureAwait(false);
        if (!isAuthorized)
        {
            throw new OperationCanceledException("Instalação cancelada ou rejeitada pelo usuário na confirmação nativa do host.");
        }

        var jobId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var ctx = new ToolInstallJobContext(jobId, key, cts);

        _installJobs[jobId] = ctx;

        _ = Task.Run(async () =>
        {
            try
            {
                switch (key)
                {
                    case "python":
                        await ExecutePythonInstallAsync(ctx, cts.Token).ConfigureAwait(false);
                        break;
                    case "node":
                        await ExecuteNodeInstallAsync(ctx, cts.Token).ConfigureAwait(false);
                        break;
                    case "whisper":
                        await ExecuteWhisperInstallAsync(ctx, cts.Token).ConfigureAwait(false);
                        break;
                    case "hyperframes":
                        await ExecuteHyperFramesInstallAsync(ctx, cts.Token).ConfigureAwait(false);
                        break;
                    default:
                        ctx.ErrorMessage = $"Instalação não suportada para {key}.";
                        ctx.IsFailed = true;
                        ctx.IsCompleted = true;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                ctx.Stage = "Cancelado";
                ctx.CurrentMessage = "Instalação cancelada pelo usuário.";
                ctx.IsCompleted = true;
                ctx.IsFailed = true;
                ctx.AddLog("Operação cancelada.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha na instalação de {ToolKey}.", key);
                ctx.Stage = "Erro";
                ctx.ErrorMessage = ex.Message;
                ctx.CurrentMessage = $"Falha durante a instalação: {ex.Message}";
                ctx.IsCompleted = true;
                ctx.IsFailed = true;
                ctx.AddLog($"Exceção: {ex}");
            }
            finally
            {
                // Instalação (mesmo parcial) pode ter mudado Python/venv/Whisper: o próximo probe deve ser real.
                _whisperService.InvalidateEnvironment();
            }
        });

        return jobId;
    }

    public ToolInstallProgressDto? GetInstallProgress(Guid jobId)
    {
        return _installJobs.TryGetValue(jobId, out var ctx) ? ctx.ToDto() : null;
    }

    public bool CancelInstall(Guid jobId)
    {
        if (_installJobs.TryGetValue(jobId, out var ctx) && !ctx.IsCompleted && ctx.CanCancel)
        {
            ctx.Cts.Cancel();
            return true;
        }
        return false;
    }

    public async Task<Guid> StartDownloadModelAsync(StartModelDownloadRequest request, CancellationToken ct = default)
    {
        if (!request.Confirmed)
        {
            throw new InvalidOperationException("Download de modelo exige confirmação explícita do usuário.");
        }

        var modelName = string.IsNullOrWhiteSpace(request.ModelName) ? "large-v3" : request.ModelName.Trim();
        if (!string.Equals(modelName, "large-v3", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(modelName, "whisper-large-v3", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Modelo '{modelName}' não é suportado pelo assistente.", nameof(request.ModelName));
        }

        bool hasActiveDownload = _downloadJobs.Values.Any(j => !j.IsCompleted);
        if (hasActiveDownload)
        {
            throw new InvalidOperationException("Já existe um download de modelo em andamento.");
        }

        var destDir = _whisperCacheDirectory;
        var dlReq = new NativeDownloadRequest(
            ModelName: modelName,
            DestinationDirectory: destDir,
            DownloadSize: "~2.9 GB"
        );

        var isDlAuthorized = await _nativeAuthorizer.RequestDownloadAuthorizationAsync(dlReq, ct).ConfigureAwait(false);
        if (!isDlAuthorized)
        {
            throw new OperationCanceledException("Download do modelo cancelado ou rejeitado pelo usuário na confirmação nativa do host.");
        }

        var jobId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var ctx = new ModelDownloadJobContext(jobId, modelName, cts);

        _downloadJobs[jobId] = ctx;

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteModelDownloadAsync(ctx, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ctx.AddLog("Operação cancelada.");
                ctx.CompleteCanceled();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha no download do modelo {ModelName}.", modelName);
                ctx.AddLog($"Exceção: {ex}");
                ctx.CompleteFailed(ex.Message);
            }
            finally
            {
                // O modelo passa a existir no cache local: o probe (modelCached) precisa ser refeito.
                _whisperService.InvalidateEnvironment();
            }
        });

        return jobId;
    }

    public ToolInstallProgressDto? GetModelDownloadProgress(Guid jobId)
    {
        return _downloadJobs.TryGetValue(jobId, out var ctx) ? ctx.ToDto() : null;
    }

    public bool CancelModelDownload(Guid jobId)
    {
        if (_downloadJobs.TryGetValue(jobId, out var ctx) && !ctx.IsCompleted && ctx.CanCancel)
        {
            ctx.Cts.Cancel();
            return true;
        }
        return false;
    }

    // -------------------------------------------------------------
    // Execuções assistidas
    // -------------------------------------------------------------

    private async Task ExecutePythonInstallAsync(ToolInstallJobContext ctx, CancellationToken ct)
    {
        var winget = ToolPathResolver.FindWinGet();
        if (winget == null)
        {
            throw new InvalidOperationException("WinGet não está disponível neste computador.");
        }

        ctx.Stage = "Instalando Python";
        ctx.ProgressPercent = 20;
        ctx.CurrentMessage = "Executando instalação silenciosa do Python 3.11 via WinGet...";
        ctx.AddLog($"Invocando {winget} install Python.Python.3.11...");

        var args = new[] { "install", "--id", "Python.Python.3.11", "-e", "--silent", "--accept-source-agreements", "--accept-package-agreements" };
        var run = await _runner.RunAsync(new ProcessRunRequest(winget, args, TimeSpan.FromMinutes(5)), ct).ConfigureAwait(false);

        ctx.AddLog($"WinGet exit code: {run.ExitCode}");
        if (run.Status != ProcessRunStatus.Completed || (run.ExitCode != 0 && run.ExitCode != 1)) // WinGet code 0 = ok, 1 = already installed in some versions
        {
            ctx.AddLog(run.StandardError);
            throw new InvalidOperationException($"Falha ao executar o WinGet para o Python (código {run.ExitCode}): {run.StandardError}");
        }

        ctx.Stage = "Verificando";
        ctx.ProgressPercent = 80;
        ctx.CurrentMessage = "Detectando interpretador instalado...";

        var diag = await DiagnosePythonAsync(ct).ConfigureAwait(false);
        if (!diag.IsReady || diag.ExecutablePath == null)
        {
            throw new InvalidOperationException("O instalador foi concluído, mas o Python não pôde ser localizado automaticamente no PATH ou em AppData.");
        }

        await _settingsService.SetToolPathAsync("python", diag.ExecutablePath, ct).ConfigureAwait(false);

        ctx.Stage = "Concluído";
        ctx.ProgressPercent = 100;
        ctx.CurrentMessage = $"Python {diag.Version} instalado e verificado em {diag.ExecutablePath}!";
        ctx.IsCompleted = true;
        ctx.IsSuccess = true;
        ctx.AddLog("Instalação do Python finalizada com sucesso.");
    }

    private async Task ExecuteNodeInstallAsync(ToolInstallJobContext ctx, CancellationToken ct)
    {
        var winget = ToolPathResolver.FindWinGet();
        if (winget == null)
        {
            throw new InvalidOperationException("WinGet não está disponível neste computador.");
        }

        ctx.Stage = "Instalando Node.js";
        ctx.ProgressPercent = 20;
        ctx.CurrentMessage = "Executando instalação silenciosa do Node.js LTS via WinGet...";
        ctx.AddLog($"Invocando {winget} install OpenJS.NodeJS.LTS...");

        var args = new[] { "install", "--id", "OpenJS.NodeJS.LTS", "-e", "--silent", "--accept-source-agreements", "--accept-package-agreements" };
        var run = await _runner.RunAsync(new ProcessRunRequest(winget, args, TimeSpan.FromMinutes(5)), ct).ConfigureAwait(false);

        ctx.AddLog($"WinGet exit code: {run.ExitCode}");
        if (run.Status != ProcessRunStatus.Completed || (run.ExitCode != 0 && run.ExitCode != 1))
        {
            ctx.AddLog(run.StandardError);
            throw new InvalidOperationException($"Falha ao executar o WinGet para o Node.js (código {run.ExitCode}): {run.StandardError}");
        }

        ctx.Stage = "Verificando";
        ctx.ProgressPercent = 80;
        ctx.CurrentMessage = "Detectando Node.js instalado...";

        var diag = await DiagnoseNodeAsync(ct).ConfigureAwait(false);
        if (!diag.IsReady || diag.ExecutablePath == null)
        {
            throw new InvalidOperationException("O Node.js foi instalado, mas ainda não foi detectado no PATH do processo.");
        }

        await _settingsService.SetToolPathAsync("node", diag.ExecutablePath, ct).ConfigureAwait(false);

        ctx.Stage = "Concluído";
        ctx.ProgressPercent = 100;
        ctx.CurrentMessage = $"Node.js {diag.Version} instalado e verificado em {diag.ExecutablePath}!";
        ctx.IsCompleted = true;
        ctx.IsSuccess = true;
        ctx.AddLog("Instalação do Node.js finalizada com sucesso.");
    }

    private async Task ExecuteWhisperInstallAsync(ToolInstallJobContext ctx, CancellationToken ct)
    {
        var pyDiag = await DiagnosePythonAsync(ct).ConfigureAwait(false);
        if (!pyDiag.IsReady || pyDiag.ExecutablePath == null)
        {
            throw new InvalidOperationException("Python não disponível. Instale o Python antes de preparar o ambiente do Whisper.");
        }

        var venvDir = ToolPathResolver.GetPersistentPythonVenvDirectory();
        var venvParent = Path.GetDirectoryName(venvDir);
        if (venvParent != null && !Directory.Exists(venvParent))
        {
            Directory.CreateDirectory(venvParent);
        }

        var venvPython = ToolPathResolver.GetPersistentPythonVenvExecutable();

        // 1. Criar venv se não existir
        if (!File.Exists(venvPython))
        {
            ctx.Stage = "Criando Virtualenv";
            ctx.ProgressPercent = 15;
            ctx.CurrentMessage = $"Criando ambiente virtual isolado em {venvDir}...";
            ctx.AddLog($"Executando {pyDiag.ExecutablePath} -m venv {venvDir}...");

            var runVenv = await _runner.RunAsync(new ProcessRunRequest(pyDiag.ExecutablePath, new[] { "-m", "venv", venvDir }, TimeSpan.FromMinutes(2)), ct).ConfigureAwait(false);
            if (runVenv.Status != ProcessRunStatus.Completed || runVenv.ExitCode != 0)
            {
                throw new InvalidOperationException($"Falha ao criar virtualenv: {runVenv.StandardError}");
            }
        }

        if (!File.Exists(venvPython))
        {
            throw new InvalidOperationException($"Executável do virtualenv não foi encontrado em {venvPython}.");
        }

        // 2. Atualizar pip
        ctx.Stage = "Atualizando Pip";
        ctx.ProgressPercent = 30;
        ctx.CurrentMessage = "Atualizando pip do ambiente virtual...";
        ctx.AddLog("Atualizando pip...");
        await _runner.RunAsync(new ProcessRunRequest(venvPython, new[] { "-m", "pip", "install", "--upgrade", "pip" }, TimeSpan.FromMinutes(3)), ct).ConfigureAwait(false);

        // 3. Instalar PyTorch
        ctx.Stage = "Instalando PyTorch";
        ctx.ProgressPercent = 50;
        ctx.CurrentMessage = "Instalando PyTorch otimizado (pode levar alguns minutos)...";
        ctx.AddLog("Instalando PyTorch (CPU)...");

        var runTorch = await _runner.RunAsync(new ProcessRunRequest(venvPython, new[] { "-m", "pip", "install", "torch", "--index-url", "https://download.pytorch.org/whl/cpu" }, TimeSpan.FromMinutes(10)), ct).ConfigureAwait(false);
        if (runTorch.Status != ProcessRunStatus.Completed || runTorch.ExitCode != 0)
        {
            ctx.AddLog(runTorch.StandardError);
            throw new InvalidOperationException($"Falha ao instalar PyTorch: {runTorch.StandardError}");
        }

        // 4. Instalar openai-whisper
        ctx.Stage = "Instalando Whisper";
        ctx.ProgressPercent = 80;
        ctx.CurrentMessage = "Instalando biblioteca openai-whisper oficial...";
        ctx.AddLog("Instalando openai-whisper...");

        var runWhisper = await _runner.RunAsync(new ProcessRunRequest(venvPython, new[] { "-m", "pip", "install", "openai-whisper" }, TimeSpan.FromMinutes(10)), ct).ConfigureAwait(false);
        if (runWhisper.Status != ProcessRunStatus.Completed || runWhisper.ExitCode != 0)
        {
            ctx.AddLog(runWhisper.StandardError);
            throw new InvalidOperationException($"Falha ao instalar openai-whisper: {runWhisper.StandardError}");
        }

        // 5. Teste probe
        ctx.Stage = "Verificando Instalação";
        ctx.ProgressPercent = 95;
        ctx.CurrentMessage = "Testando probe do worker Whisper...";

        // Descarta o probe em cache: o resultado abaixo (e o cache do pipeline) refletem a instalação recém-feita.
        _whisperService.InvalidateEnvironment();
        var probe = await _whisperService.ProbeEnvironmentAsync(ct).ConfigureAwait(false);
        ctx.AddLog($"Resultado probe: {probe.StatusSummary}");

        ctx.Stage = "Concluído";
        ctx.ProgressPercent = 100;
        ctx.CurrentMessage = "Ambiente Whisper e dependências instalados com sucesso!";
        ctx.IsCompleted = true;
        ctx.IsSuccess = true;
        ctx.AddLog("Instalação do Whisper finalizada com sucesso.");
    }

    private async Task ExecuteHyperFramesInstallAsync(ToolInstallJobContext ctx, CancellationToken ct)
    {
        var nodeDiag = await DiagnoseNodeAsync(ct).ConfigureAwait(false);
        if (!nodeDiag.IsReady || nodeDiag.ExecutablePath == null)
        {
            throw new InvalidOperationException("Node.js não disponível. Instale o Node.js antes de configurar o HyperFrames.");
        }

        // Exigir Node.js 22 ou superior (requisito de segurança e compatibilidade)
        if (nodeDiag.Version != null)
        {
            var match = Regex.Match(nodeDiag.Version, @"(?:v)?(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var majorVersion) && majorVersion < 22)
            {
                throw new InvalidOperationException($"O HyperFrames requer Node.js versão 22 ou superior. Versão detectada: {nodeDiag.Version}. Atualize o Node.js primeiro.");
            }
        }

        var targetDir = ToolPathResolver.GetPersistentHyperFramesDirectory();
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var npmPath = ToolPathResolver.FindInPath("npm.cmd") ?? ToolPathResolver.FindInPath("npm.exe");
        if (npmPath == null)
        {
            // Tenta localizar npm ao lado do node.exe
            var nodeDir = Path.GetDirectoryName(nodeDiag.ExecutablePath);
            var candNpm = Path.Combine(nodeDir!, "npm.cmd");
            if (File.Exists(candNpm)) npmPath = candNpm;
        }

        if (npmPath == null)
        {
            throw new InvalidOperationException("Executável 'npm' não encontrado.");
        }

        ctx.Stage = "Instalando HyperFrames";
        ctx.ProgressPercent = 30;
        ctx.CurrentMessage = $"Executando 'npm install hyperframes@0.9.1' no diretório persistente {targetDir}...";
        ctx.AddLog($"Invocando {npmPath} install hyperframes@0.9.1 --prefix {targetDir}...");

        var runNpm = await _runner.RunAsync(
            new ProcessRunRequest(
                npmPath,
                new[] { "install", "hyperframes@0.9.1", "--prefix", targetDir, "--no-fund", "--no-audit", "--ignore-scripts" },
                TimeSpan.FromMinutes(5),
                WorkingDirectory: targetDir
            ),
            ct
        ).ConfigureAwait(false);

        if (runNpm.Status != ProcessRunStatus.Completed || runNpm.ExitCode != 0)
        {
            ctx.AddLog(runNpm.StandardError);
            throw new InvalidOperationException($"Falha na instalação do pacote hyperframes via npm: {runNpm.StandardError}");
        }

        ctx.Stage = "Localizando CLI";
        ctx.ProgressPercent = 80;
        ctx.CurrentMessage = "Detectando executável CLI do HyperFrames e executando teste diagnóstico...";

        var candidates = ToolPathResolver.FindHyperFramesCandidates(null);
        var found = candidates.FirstOrDefault();

        if (found == null || !File.Exists(found))
        {
            throw new InvalidOperationException("O pacote foi instalado, mas o arquivo dist/cli.js do HyperFrames não foi encontrado.");
        }

        // Teste diagnóstico real do CLI (--version e --help)
        var testRun = await _runner.RunAsync(
            new ProcessRunRequest(nodeDiag.ExecutablePath, new[] { found, "--version" }, TimeSpan.FromSeconds(10)),
            ct
        ).ConfigureAwait(false);

        if (testRun.Status != ProcessRunStatus.Completed || testRun.ExitCode != 0)
        {
            throw new InvalidOperationException($"Falha no diagnóstico funcional do HyperFrames CLI recém-instalado (--version): {testRun.StandardError}");
        }

        var helpRun = await _runner.RunAsync(
            new ProcessRunRequest(nodeDiag.ExecutablePath, new[] { found, "--help" }, TimeSpan.FromSeconds(10)),
            ct
        ).ConfigureAwait(false);

        if (helpRun.Status != ProcessRunStatus.Completed || helpRun.ExitCode != 0)
        {
            throw new InvalidOperationException($"Falha no diagnóstico funcional do HyperFrames CLI recém-instalado (--help): {helpRun.StandardError}");
        }

        await _settingsService.SetToolPathAsync("hyperframes", found, ct).ConfigureAwait(false);

        ctx.Stage = "Concluído";
        ctx.ProgressPercent = 100;
        ctx.CurrentMessage = $"HyperFrames configurado com sucesso em {found} (versão {testRun.StandardOutput.Trim()})!";
        ctx.IsCompleted = true;
        ctx.IsSuccess = true;
        ctx.AddLog("Instalação do HyperFrames finalizada e verificada com sucesso.");
    }

    private static async Task<bool> ExistingModelMatchesAsync(string filePath, string expectedSha256, CancellationToken ct)
    {
        try
        {
            var actual = await HttpModelDownloader.ComputeFileSha256Async(filePath, ct).ConfigureAwait(false);
            return string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task ExecuteModelDownloadAsync(ModelDownloadJobContext ctx, CancellationToken ct)
    {
        var modelName = ctx.ModelName;
        var destinationDir = _whisperCacheDirectory;
        if (!Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        var destinationFile = Path.Combine(destinationDir, $"{modelName}.pt");

        string url;
        string expectedSha256;

        if (string.Equals(modelName, "large-v3", StringComparison.OrdinalIgnoreCase))
        {
            url = ToolPathResolver.OfficialWhisperLargeV3Url;
            expectedSha256 = ToolPathResolver.OfficialWhisperLargeV3Sha256;
        }
        else
        {
            throw new NotSupportedException($"Download automático não suportado para o modelo '{modelName}'.");
        }

        // Preservar modelo já existente se o hash SHA-256 já for válido
        if (File.Exists(destinationFile))
        {
            ctx.AddLog("Verificando integridade de modelo já existente em disco...");
            lock (ctx.Lock)
            {
                // Hash de ~3 GB sem percentual real: a UI mostra estágio indeterminado. Continua cancelável.
                ctx.Stage = "Verificando SHA-256";
                ctx.ProgressPercent = null;
                ctx.CurrentMessage = $"Verificando o modelo {modelName} já presente em disco (SHA-256)...";
            }
            if (await ExistingModelMatchesAsync(destinationFile, expectedSha256, ct).ConfigureAwait(false))
            {
                ctx.AddLog("Modelo pré-existente íntegro. Download dispensado.");
                ctx.CompleteSuccess($"Modelo {modelName} já está instalado e íntegro com hash SHA-256 verificado!");
                return;
            }
            ctx.AddLog("Arquivo existente está corrompido ou incompleto. Iniciando novo download seguro...");
        }

        lock (ctx.Lock)
        {
            ctx.Stage = "Baixando Modelo";
            ctx.ProgressPercent = null;
            ctx.CurrentMessage = $"Conectando para baixar o modelo {modelName}...";
        }
        ctx.AddLog($"Iniciando download seguro de {url} para {destinationFile}...");

        var progress = new SynchronousProgress<DownloadProgressUpdate>(ctx.ApplyDownloadProgress);

        await _modelDownloader.DownloadFileAsync(url, destinationFile, expectedSha256, progress, ct).ConfigureAwait(false);

        ctx.AddLog("Download finalizado e verificado com sucesso.");
        ctx.CompleteSuccess($"Modelo {modelName} baixado e validado com sucesso com hash SHA-256 oficial ({expectedSha256.Substring(0, 12)}...)!");
    }
}
