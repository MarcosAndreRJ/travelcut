using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Tooling;

public sealed partial class EnvironmentAssistantService : IEnvironmentAssistantService
{
    private readonly IProcessRunner _runner;
    private readonly IWhisperTranscriptionService _whisperService;
    private readonly ITravelCutSettingsService _settingsService;
    private readonly IModelDownloader _modelDownloader;
    private readonly ILogger<EnvironmentAssistantService> _logger;
    private readonly TimeProvider _time;
    private readonly string _whisperCacheDirectory;
    private readonly INativeInstallationAuthorizer _nativeAuthorizer;

    private readonly ConcurrentDictionary<Guid, ToolInstallJobContext> _installJobs = new();
    private readonly ConcurrentDictionary<Guid, ModelDownloadJobContext> _downloadJobs = new();
    private sealed record PlanAuthorization(string ToolKey, DateTimeOffset ExpiresAtUtc);
    private readonly ConcurrentDictionary<string, PlanAuthorization> _planAuthorizations = new();

    public EnvironmentAssistantService(
        IProcessRunner runner,
        IWhisperTranscriptionService whisperService,
        ITravelCutSettingsService settingsService,
        IModelDownloader modelDownloader,
        ILogger<EnvironmentAssistantService> logger,
        TimeProvider? time = null,
        INativeInstallationAuthorizer? nativeAuthorizer = null,
        string? whisperCacheDirectory = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _whisperService = whisperService ?? throw new ArgumentNullException(nameof(whisperService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _modelDownloader = modelDownloader ?? throw new ArgumentNullException(nameof(modelDownloader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nativeAuthorizer = nativeAuthorizer ?? DenyAllNativeInstallationAuthorizer.Instance;
        _time = time ?? TimeProvider.System;
        _whisperCacheDirectory = string.IsNullOrWhiteSpace(whisperCacheDirectory)
            ? ToolPathResolver.GetWhisperCacheDir()
            : whisperCacheDirectory;
    }

    public string WhisperCacheDirectory => _whisperCacheDirectory;
    public string GetModelPathFor(string modelName = "large-v3") => Path.Combine(_whisperCacheDirectory, $"{modelName}.pt");

    public static string GetWhisperCacheDir()
    {
        var xdg = System.Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, "whisper");
        }
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".cache", "whisper");
    }

    public static string GetModelPath(string modelName = "large-v3")
    {
        return Path.Combine(GetWhisperCacheDir(), $"{modelName}.pt");
    }

    public async Task<EnvironmentAssistantDiagnosticDto> DiagnoseAsync(CancellationToken ct = default)
    {
        var python = await DiagnosePythonAsync(ct).ConfigureAwait(false);
        var whisper = await DiagnoseWhisperAsync(ct).ConfigureAwait(false);
        var ffmpeg = await DiagnoseFfmpegAsync(ct).ConfigureAwait(false);
        var node = await DiagnoseNodeAsync(ct).ConfigureAwait(false);
        var hyperframes = await DiagnoseHyperFramesAsync(node, ct).ConfigureAwait(false);

        var tools = new List<ToolDiagnosticDto> { python, whisper, ffmpeg, node, hyperframes };

        var allReady = python.IsReady
            && whisper.IsReady
            && ffmpeg.IsReady
            && node.IsReady
            && hyperframes.IsReady;

        var wingetPath = ToolPathResolver.FindWinGet();

        return new EnvironmentAssistantDiagnosticDto(
            Tools: tools,
            AllReady: allReady,
            EvaluatedAtUtc: _time.GetUtcNow().UtcDateTime,
            SystemInfo: new SystemInfoDto(
                OsDescription: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Architecture: System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                IsWindows: true,
                TotalMemoryMb: 8192,
                FreeDiskSpaceMb: 20480,
                TargetCacheDirectory: _whisperCacheDirectory,
                HasWinget: wingetPath != null,
                WingetPath: wingetPath
            )
        );
    }

    public async Task<ToolDiagnosticDto> DiagnoseToolAsync(string toolKey, CancellationToken ct = default)
    {
        return toolKey.ToLowerInvariant() switch
        {
            "python" => await DiagnosePythonAsync(ct).ConfigureAwait(false),
            "whisper" => await DiagnoseWhisperAsync(ct).ConfigureAwait(false),
            "ffmpeg" => await DiagnoseFfmpegAsync(ct).ConfigureAwait(false),
            "node" => await DiagnoseNodeAsync(ct).ConfigureAwait(false),
            "hyperframes" => await DiagnoseHyperFramesAsync(await DiagnoseNodeAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false),
            _ => throw new ArgumentException($"Ferramenta desconhecida '{toolKey}'.", nameof(toolKey))
        };
    }

    public async Task<AutoLocateResponse> AutoLocateToolAsync(string toolKey, CancellationToken ct = default)
    {
        var key = toolKey.ToLowerInvariant();
        var configured = await _settingsService.GetToolPathAsync(key, ct).ConfigureAwait(false);

        var candidates = key switch
        {
            "python" => ToolPathResolver.FindPythonCandidates(configured),
            "ffmpeg" => ToolPathResolver.FindFfmpegCandidates(configured),
            "node" => ToolPathResolver.FindNodeCandidates(configured),
            "hyperframes" => ToolPathResolver.FindHyperFramesCandidates(configured),
            _ => new List<string>()
        };

        return new AutoLocateResponse(toolKey, candidates);
    }

    public async Task<bool> ConfigureToolPathAsync(string toolKey, string path, CancellationToken ct = default)
    {
        var key = toolKey.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path) && !Directory.Exists(path))
        {
            return false;
        }

        await _settingsService.SetToolPathAsync(key, path, ct).ConfigureAwait(false);
        _whisperService.InvalidateEnvironment();
        return true;
    }

    public async Task<TestToolResponse> TestToolAsync(string toolKey, CancellationToken ct = default)
    {
        var key = toolKey.ToLowerInvariant();
        var logs = new List<string>();

        try
        {
            switch (key)
            {
                case "python":
                {
                    var diag = await DiagnosePythonAsync(ct).ConfigureAwait(false);
                    if (!diag.IsReady || diag.ExecutablePath == null)
                    {
                        return new TestToolResponse(key, false, "Python não está pronto para teste.", null, logs);
                    }

                    var run = await _runner.RunAsync(new ProcessRunRequest(diag.ExecutablePath, new[] { "-c", "import sys, math; print(f'TEST_OK:math.pi={math.pi}')" }, TimeSpan.FromSeconds(10)), ct).ConfigureAwait(false);
                    logs.Add($"stdout: {run.StandardOutput}");
                    logs.Add($"stderr: {run.StandardError}");
                    var ok = run.Status == ProcessRunStatus.Completed && run.ExitCode == 0 && run.StandardOutput.Contains("TEST_OK");
                    return new TestToolResponse(key, ok, ok ? "Python testado com sucesso." : "Falha ao executar teste do Python.", run.StandardOutput, logs);
                }

                case "whisper":
                {
                    var probe = await _whisperService.ProbeEnvironmentAsync(ct).ConfigureAwait(false);
                    logs.Add($"Probe status: {probe.StatusSummary}");
                    logs.Add($"HasPython: {probe.HasPython}, HasWhisper: {probe.HasWhisper}, Device: {probe.Device}, HasCuda: {probe.HasCuda}");

                    var cached = File.Exists(GetModelPath(probe.DefaultModel));
                    logs.Add($"Modelo {probe.DefaultModel} em cache: {cached}");

                    var ok = probe.HasPython && probe.HasWhisper && cached;
                    var msg = ok ? $"Whisper pronto no dispositivo {probe.Device ?? "CPU"}." : $"Whisper incompleto: {probe.StatusSummary}";
                    return new TestToolResponse(key, ok, msg, probe.StatusSummary, logs);
                }

                case "ffmpeg":
                {
                    var diag = await DiagnoseFfmpegAsync(ct).ConfigureAwait(false);
                    if (!diag.IsReady || diag.ExecutablePath == null)
                    {
                        return new TestToolResponse(key, false, "FFmpeg não está pronto para teste.", null, logs);
                    }

                    var run = await _runner.RunAsync(new ProcessRunRequest(diag.ExecutablePath, new[] { "-version" }, TimeSpan.FromSeconds(10)), ct).ConfigureAwait(false);
                    logs.Add(run.StandardOutput);
                    var ok = run.Status == ProcessRunStatus.Completed && run.ExitCode == 0;
                    return new TestToolResponse(key, ok, ok ? "FFmpeg testado com sucesso." : "Falha ao executar FFmpeg.", run.StandardOutput, logs);
                }

                case "node":
                {
                    var diag = await DiagnoseNodeAsync(ct).ConfigureAwait(false);
                    if (!diag.IsReady || diag.ExecutablePath == null)
                    {
                        return new TestToolResponse(key, false, "Node.js não está pronto para teste.", null, logs);
                    }

                    var run = await _runner.RunAsync(new ProcessRunRequest(diag.ExecutablePath, new[] { "-e", "console.log('NODE_TEST_OK:' + process.version)" }, TimeSpan.FromSeconds(10)), ct).ConfigureAwait(false);
                    logs.Add(run.StandardOutput);
                    var ok = run.Status == ProcessRunStatus.Completed && run.ExitCode == 0 && run.StandardOutput.Contains("NODE_TEST_OK");
                    return new TestToolResponse(key, ok, ok ? "Node.js testado com sucesso." : "Falha ao executar Node.js.", run.StandardOutput, logs);
                }

                case "hyperframes":
                {
                    var nodeDiag = await DiagnoseNodeAsync(ct).ConfigureAwait(false);
                    var diag = await DiagnoseHyperFramesAsync(nodeDiag, ct).ConfigureAwait(false);
                    if (!diag.IsReady || diag.ExecutablePath == null)
                    {
                        return new TestToolResponse(key, false, "HyperFrames não está pronto para teste.", null, logs);
                    }

                    if (diag.ExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var run = await _runner.RunAsync(new ProcessRunRequest(diag.ExecutablePath, new[] { "--version" }, TimeSpan.FromSeconds(10)), ct).ConfigureAwait(false);
                        logs.Add(run.StandardOutput);
                        var ok = run.Status == ProcessRunStatus.Completed && run.ExitCode == 0;
                        return new TestToolResponse(key, ok, ok ? $"HyperFrames CLI ({diag.Version}) testado com sucesso." : "Falha ao executar CLI.", run.StandardOutput, logs);
                    }
                    else
                    {
                        if (!nodeDiag.IsReady || nodeDiag.ExecutablePath == null)
                        {
                            return new TestToolResponse(key, false, "Node.js é necessário para testar o script HyperFrames.", null, logs);
                        }

                        var run = await _runner.RunAsync(new ProcessRunRequest(nodeDiag.ExecutablePath, new[] { diag.ExecutablePath, "--version" }, TimeSpan.FromSeconds(10)), ct).ConfigureAwait(false);
                        logs.Add(run.StandardOutput);
                        var ok = run.Status == ProcessRunStatus.Completed && run.ExitCode == 0;
                        return new TestToolResponse(key, ok, ok ? $"HyperFrames CLI ({diag.Version}) testado com sucesso via Node." : "Falha ao executar CLI via Node.", run.StandardOutput, logs);
                    }
                }

                default:
                    return new TestToolResponse(key, false, $"Ferramenta '{toolKey}' desconhecida.", null, logs);
            }
        }
        catch (Exception ex)
        {
            logs.Add($"Erro ao testar {toolKey}: {ex.Message}");
            return new TestToolResponse(key, false, $"Erro inesperado: {ex.Message}", ex.ToString(), logs);
        }
    }

    // -------------------------------------------------------------
    // Diagnósticos individuais
    // -------------------------------------------------------------

    private async Task<ToolDiagnosticDto> DiagnosePythonAsync(CancellationToken ct)
    {
        var actions = new List<string> { "refresh", "locate", "configure_path" };
        var configured = await _settingsService.GetToolPathAsync("python", ct).ConfigureAwait(false);
        var candidates = ToolPathResolver.FindPythonCandidates(configured);

        string? validExe = null;
        string? version = null;

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;

            var run = await _runner.RunAsync(new ProcessRunRequest(candidate, new[] { "-c", "import sys; print(f'PYTHON_OK:{sys.version_info[0]}.{sys.version_info[1]}.{sys.version_info[2]}')" }, TimeSpan.FromSeconds(5)), ct).ConfigureAwait(false);
            if (run.Status == ProcessRunStatus.Completed && run.ExitCode == 0 && run.StandardOutput.Contains("PYTHON_OK:"))
            {
                var match = Regex.Match(run.StandardOutput, @"PYTHON_OK:(\d+\.\d+\.\d+)");
                if (match.Success)
                {
                    validExe = candidate;
                    version = match.Groups[1].Value;
                    break;
                }
            }
        }

        if (validExe != null)
        {
            actions.Add("test");
            return new ToolDiagnosticDto(
                ToolKey: "python",
                DisplayName: "Python 3",
                State: ToolEnvironmentState.Ready,
                IsReady: true,
                Version: version,
                ExecutablePath: validExe,
                Details: $"Python {version} funcional localizado em {validExe}.",
                SupportedActions: actions,
                WinGetPackageId: "Python.Python.3.11"
            );
        }

        actions.Add("install");
        return new ToolDiagnosticDto(
            ToolKey: "python",
            DisplayName: "Python 3",
            State: ToolEnvironmentState.NotInstalled,
            IsReady: false,
            Version: null,
            ExecutablePath: null,
            Details: "Python 3.8+ não encontrado no sistema ou não configurado.",
            SupportedActions: actions,
            WinGetPackageId: "Python.Python.3.11"
        );
    }

    private async Task<ToolDiagnosticDto> DiagnoseWhisperAsync(CancellationToken ct)
    {
        var actions = new List<string> { "refresh" };
        var probe = await _whisperService.ProbeEnvironmentAsync(ct).ConfigureAwait(false);

        var modelPath = GetModelPathFor(probe.DefaultModel);
        var modelCached = File.Exists(modelPath);

        string state;
        bool isReady = false;
        string details;

        var persistentVenvDir = ToolPathResolver.GetPersistentPythonVenvDirectory();
        var localVenvDir = Path.Combine(AppContext.BaseDirectory, "workers", ".venv");
        var venvExists = Directory.Exists(persistentVenvDir) || Directory.Exists(localVenvDir);

        bool modelValid = false;
        if (modelCached)
        {
            try
            {
                var fi = new FileInfo(modelPath);
                // large-v3.pt tem aproximadamente 2.88 GB
                modelValid = fi.Length > 2_500_000_000L;
            }
            catch { }
        }

        if (!probe.HasPython)
        {
            state = ToolEnvironmentState.NotInstalled;
            details = "Python não encontrado para o Whisper. Instale o Python primeiro.";
        }
        else if (!venvExists && !probe.HasWhisper)
        {
            state = ToolEnvironmentState.InstalledNotFound;
            details = "Ambiente virtual dedicado (python-venv) não foi criado.";
            actions.Add("install");
        }
        else if (!probe.HasWhisper)
        {
            state = ToolEnvironmentState.DependencyMissing;
            details = "Pacote openai-whisper ou PyTorch ausente no ambiente Python.";
            actions.Add("install");
        }
        else if (!modelCached)
        {
            state = ToolEnvironmentState.ModelMissing;
            details = $"Whisper instalado, mas o modelo {probe.DefaultModel} não está no cache local.";
            actions.Add("download_model");
        }
        else if (!modelValid)
        {
            state = ToolEnvironmentState.ModelMissing;
            details = $"O modelo {probe.DefaultModel} em cache está corrompido ou incompleto. Necessário baixar novamente.";
            actions.Add("download_model");
        }
        else
        {
            if (probe.HasCuda == true)
            {
                state = ToolEnvironmentState.ReadyGpu;
                details = $"Whisper pronto com aceleração CUDA GPU ({probe.Device ?? "CUDA"}).";
            }
            else
            {
                state = ToolEnvironmentState.ReadyCpu;
                details = "Whisper pronto em modo CPU.";
            }
            isReady = true;
            actions.Add("test");
        }

        return new ToolDiagnosticDto(
            ToolKey: "whisper",
            DisplayName: "OpenAI Whisper & PyTorch",
            State: state,
            IsReady: isReady,
            Version: probe.Engine ?? "large-v3",
            ExecutablePath: modelCached ? modelPath : null,
            Details: details,
            SupportedActions: actions,
            Metadata: new Dictionary<string, string>
            {
                ["device"] = probe.Device ?? (probe.HasCuda == true ? "cuda" : "cpu"),
                ["model"] = probe.DefaultModel,
                ["modelCached"] = modelCached.ToString(),
                ["cacheDir"] = _whisperCacheDirectory,
                ["engine"] = probe.Engine ?? "openai-whisper"
            }
        );
    }

    private async Task<ToolDiagnosticDto> DiagnoseFfmpegAsync(CancellationToken ct)
    {
        var actions = new List<string> { "refresh", "locate", "configure_path" };
        var configured = await _settingsService.GetToolPathAsync("ffmpeg", ct).ConfigureAwait(false);
        var candidates = ToolPathResolver.FindFfmpegCandidates(configured);

        string? validExe = null;
        string? version = null;

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;

            var run = await _runner.RunAsync(new ProcessRunRequest(candidate, new[] { "-version" }, TimeSpan.FromSeconds(5)), ct).ConfigureAwait(false);
            if (run.Status == ProcessRunStatus.Completed && run.ExitCode == 0 && run.StandardOutput.Contains("ffmpeg version"))
            {
                var match = Regex.Match(run.StandardOutput, @"ffmpeg version\s+([^\s]+)");
                validExe = candidate;
                version = match.Success ? match.Groups[1].Value : "detectado";
                break;
            }
        }

        if (validExe != null)
        {
            actions.Add("test");
            return new ToolDiagnosticDto(
                ToolKey: "ffmpeg",
                DisplayName: "FFmpeg & FFprobe",
                State: ToolEnvironmentState.Ready,
                IsReady: true,
                Version: version,
                ExecutablePath: validExe,
                Details: $"FFmpeg {version} localizado em {validExe}.",
                SupportedActions: actions
            );
        }

        return new ToolDiagnosticDto(
            ToolKey: "ffmpeg",
            DisplayName: "FFmpeg & FFprobe",
            State: ToolEnvironmentState.NotInstalled,
            IsReady: false,
            Version: null,
            ExecutablePath: null,
            Details: "FFmpeg não encontrado no PATH ou locais padrão. Configure o caminho manualmente.",
            SupportedActions: actions
        );
    }

    private async Task<ToolDiagnosticDto> DiagnoseNodeAsync(CancellationToken ct)
    {
        var actions = new List<string> { "refresh", "locate", "configure_path" };
        var configured = await _settingsService.GetToolPathAsync("node", ct).ConfigureAwait(false);
        var candidates = ToolPathResolver.FindNodeCandidates(configured);

        string? validExe = null;
        string? version = null;

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;

            var run = await _runner.RunAsync(new ProcessRunRequest(candidate, new[] { "--version" }, TimeSpan.FromSeconds(5)), ct).ConfigureAwait(false);
            if (run.Status == ProcessRunStatus.Completed && run.ExitCode == 0 && run.StandardOutput.Trim().StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                validExe = candidate;
                version = run.StandardOutput.Trim();
                break;
            }
        }

        if (validExe != null)
        {
            actions.Add("test");
            return new ToolDiagnosticDto(
                ToolKey: "node",
                DisplayName: "Node.js LTS",
                State: ToolEnvironmentState.Ready,
                IsReady: true,
                Version: version,
                ExecutablePath: validExe,
                Details: $"Node.js {version} localizado em {validExe}.",
                SupportedActions: actions,
                WinGetPackageId: "OpenJS.NodeJS.LTS"
            );
        }

        actions.Add("install");
        return new ToolDiagnosticDto(
            ToolKey: "node",
            DisplayName: "Node.js LTS",
            State: ToolEnvironmentState.NotInstalled,
            IsReady: false,
            Version: null,
            ExecutablePath: null,
            Details: "Node.js LTS não encontrado no PATH ou locais padrão.",
            SupportedActions: actions,
            WinGetPackageId: "OpenJS.NodeJS.LTS"
        );
    }

    private async Task<ToolDiagnosticDto> DiagnoseHyperFramesAsync(ToolDiagnosticDto nodeDiag, CancellationToken ct)
    {
        var actions = new List<string> { "refresh", "locate", "configure_path" };
        var configured = await _settingsService.GetToolPathAsync("hyperframes", ct).ConfigureAwait(false);
        var candidates = ToolPathResolver.FindHyperFramesCandidates(configured);

        if (!nodeDiag.IsReady)
        {
            return new ToolDiagnosticDto(
                ToolKey: "hyperframes",
                DisplayName: "HyperFrames Motion Renderer",
                State: ToolEnvironmentState.DependencyMissing,
                IsReady: false,
                Version: null,
                ExecutablePath: null,
                Details: "Node.js é obrigatório para execução do HyperFrames.",
                SupportedActions: actions
            );
        }

        string? validPath = null;
        string? version = null;

        foreach (var candidate in candidates)
        {
            if (!File.Exists(candidate)) continue;

            ProcessRunResult run;
            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                run = await _runner.RunAsync(new ProcessRunRequest(candidate, new[] { "--version" }, TimeSpan.FromSeconds(5)), ct).ConfigureAwait(false);
            }
            else
            {
                if (nodeDiag.ExecutablePath == null) continue;
                run = await _runner.RunAsync(new ProcessRunRequest(nodeDiag.ExecutablePath, new[] { candidate, "--version" }, TimeSpan.FromSeconds(5)), ct).ConfigureAwait(false);
            }

            if (run.Status == ProcessRunStatus.Completed && run.ExitCode == 0)
            {
                validPath = candidate;
                var match = Regex.Match(run.StandardOutput, @"\d+\.\d+\.\d+");
                version = match.Success ? match.Value : run.StandardOutput.Trim();
                break;
            }
        }

        if (validPath != null)
        {
            actions.Add("test");
            return new ToolDiagnosticDto(
                ToolKey: "hyperframes",
                DisplayName: "HyperFrames Motion Renderer",
                State: ToolEnvironmentState.Ready,
                IsReady: true,
                Version: version,
                ExecutablePath: validPath,
                Details: $"HyperFrames CLI {version} localizado em {validPath}.",
                SupportedActions: actions
            );
        }

        actions.Add("install");
        return new ToolDiagnosticDto(
            ToolKey: "hyperframes",
            DisplayName: "HyperFrames Motion Renderer",
            State: ToolEnvironmentState.NotInstalled,
            IsReady: false,
            Version: null,
            ExecutablePath: null,
            Details: "HyperFrames não configurado nem encontrado nos locais padrão. Pode ser instalado via npm ou configurado manualmente.",
            SupportedActions: actions
        );
    }
}
