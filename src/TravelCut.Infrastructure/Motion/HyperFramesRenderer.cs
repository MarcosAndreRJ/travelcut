using System.Text.RegularExpressions;
using TravelCut.Application.Motion;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Motion;

/// <summary>
/// Renderizador HyperFrames (F09.5) por subprocesso, sem shell (ProcessRunner + ArgumentList).
///
/// Detecção real, em ordem:
/// (1) Node.js: caminho absoluto de <c>TRAVELCUT_NODE_PATH</c> (arquivo existente) ou o primeiro <c>node</c> encontrado
///     nas entradas absolutas do PATH (o diretório atual nunca é consultado); depois <c>&lt;node&gt; --version</c>;
/// (2) CLI em TRAVELCUT_HYPERFRAMES_CLI (arquivo existente, não <c>.cmd</c>/<c>.bat</c>/<c>.ps1</c>);
/// (3) <c>&lt;cli&gt; --version</c> com timeout curto. Só então o estado é READY, com a versão lida.
///
/// SUPOSIÇÕES NÃO VERIFICADAS (a confirmar no host, ver docs/20-HYPERFRAMES.md):
/// - o CLI aceita <c>--version</c> e imprime a versão na primeira linha (ex.: "1.2.3" ou "hyperframes 1.2.3");
/// - o render é invocado como <c>render --manifest &lt;request.json&gt; --assets-root &lt;2-Assets&gt; --output &lt;output.mp4&gt;</c>;
/// - sucesso é código 0 E arquivo de saída não vazio existente. Código 0 sem arquivo = OUTPUT_MISSING (erro, não sucesso).
/// Se o CLI responder diferente, o resultado é ERROR com código explícito; nunca READY_REAL sem arquivo.
///
/// Regras: sem download, sem npx, sem instalação; saída só em 3-Trabalho/motion/&lt;renderId&gt; e somente se nenhum
/// segmento desde o projeto for reparse point (checado antes de criar ou apagar qualquer coisa);
/// mensagens sem caminhos absolutos (stderr é sanitizado e truncado).
/// </summary>
public sealed class HyperFramesRenderer : IMotionRenderer
{
    public const string ModeName = "hyperframes";
    public const string NodeExecutable = "node";
    public const string VersionArgument = "--version";
    public const string RenderSubcommand = "render";
    public const string ManifestOption = "--manifest";
    public const string AssetsRootOption = "--assets-root";
    public const string OutputOption = "--output";
    public const string SupportedNote = "Template previsto. Formato do CLI do HyperFrames não verificado nesta VM: confirmar no host (docs/20-HYPERFRAMES.md).";
    public const string VerificationPending = "host_pending";
    public const string UnsupportedCliTypeCode = "CLI_UNSUPPORTED_TYPE";
    public const string NodePathNotFoundCode = "NODE_PATH_NOT_FOUND";

    private static readonly string[] ScriptExtensions = { ".js", ".mjs", ".cjs" };
    private static readonly string[] UnsupportedWrapperExtensions = { ".cmd", ".bat", ".ps1" };
    private static readonly Regex NodeVersionPattern = new(@"^v?\d+\.\d+\.\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex CliVersionPattern =
        new(@"^(?:[A-Za-z][\w.-]*\s+)?v?(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)$", RegexOptions.CultureInvariant);

    // Caminhos absolutos (Windows, UNC e POSIX com pelo menos dois segmentos) são ocultados das mensagens.
    private static readonly Regex AbsolutePathPattern = new(
        @"[A-Za-z]:[\\/][^\s""'<>|]*|\\\\[^\s""'<>|]+|(?<![\w.])/[\w.\-]+/[^\s""']*",
        RegexOptions.CultureInvariant);

    private const int MaxMessageLength = 160;

    private readonly IProcessRunner _runner;
    private readonly HyperFramesOptions _options;
    private readonly TimeProvider _time;

    public HyperFramesRenderer(IProcessRunner runner, HyperFramesOptions options, TimeProvider? time = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _time = time ?? TimeProvider.System;
    }

    public string Mode => ModeName;

    public async Task<MotionCapabilitiesDto> DetectCapabilitiesAsync(CancellationToken ct = default)
    {
        var detection = await DetectInternalAsync(ct).ConfigureAwait(false);
        return detection.Capabilities;
    }

    public async Task<MotionRenderResultDto> RenderAsync(MotionRenderRequestDto request, MotionRenderContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        DateTime started = _time.GetUtcNow().UtcDateTime;
        string manifestRelative = MotionPaths.ManifestRelative(context.RenderId);

        var detection = await DetectInternalAsync(ct).ConfigureAwait(false);
        var caps = detection.Capabilities;

        if (caps.EnvironmentState != MotionEnvironmentStates.Ready || detection.CliPath is null || detection.NodePath is null)
        {
            string status = caps.EnvironmentState == MotionEnvironmentStates.Error
                ? MotionRenderStatuses.Error
                : MotionRenderStatuses.Unavailable;
            return Failure(status, caps.ReasonCode ?? caps.EnvironmentState, caps.StatusMessage, manifestRelative, started);
        }

        string workRoot = Path.Combine(context.ProjectRoot, "3-Trabalho", "motion");
        if (!MotionPaths.IsInside(workRoot, context.OutputDirAbsolute))
        {
            return Failure(MotionRenderStatuses.Error, "CONTEXT_INVALID",
                "Contexto de render fora de 3-Trabalho/motion.", manifestRelative, started);
        }

        // Antes de criar ou apagar qualquer coisa: nenhum segmento (pasta do render, arquivo de saída) pode ser link.
        if (MotionPaths.HasReparsePointBetween(context.ProjectRoot, context.OutputFileAbsolute))
        {
            return Failure(MotionRenderStatuses.Error, MotionRequestValidator.PermissionDeniedCode,
                "Pasta de saída do render contém link, junção ou atalho. Nada foi escrito.", manifestRelative, started);
        }

        if (!File.Exists(context.ManifestAbsolutePath))
        {
            return Failure(MotionRenderStatuses.Error, "CONTEXT_INVALID",
                "Manifesto do render ausente.", manifestRelative, started);
        }

        try
        {
            Directory.CreateDirectory(context.OutputDirAbsolute);

            // Saída antiga do mesmo renderId não pode ser confundida com a saída desta execução.
            if (File.Exists(context.OutputFileAbsolute))
                File.Delete(context.OutputFileAbsolute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failure(MotionRenderStatuses.Error, "OUTPUT_DIR_UNAVAILABLE",
                "Não foi possível preparar a pasta de saída em 3-Trabalho/motion.", manifestRelative, started);
        }

        var invocation = BuildInvocation(detection.NodePath, detection.CliPath, new[]
        {
            RenderSubcommand,
            ManifestOption, context.ManifestAbsolutePath,
            AssetsRootOption, context.AssetsRoot,
            OutputOption, context.OutputFileAbsolute,
        });

        var run = await _runner.RunAsync(
            new ProcessRunRequest(invocation.FileName, invocation.Arguments, _options.RenderTimeout),
            ct).ConfigureAwait(false);

        switch (run.Status)
        {
            case ProcessRunStatus.NotStarted:
                return Failure(MotionRenderStatuses.Error, "CLI_NOT_EXECUTABLE",
                    "O CLI configurado não pôde ser executado.", manifestRelative, started);
            case ProcessRunStatus.TimedOut:
                return Failure(MotionRenderStatuses.Error, "RENDER_TIMEOUT",
                    "Tempo limite do render excedido.", manifestRelative, started);
            case ProcessRunStatus.Canceled:
                return Failure(MotionRenderStatuses.Error, "RENDER_CANCELED",
                    "Render cancelado.", manifestRelative, started);
        }

        if (run.ExitCode != 0)
        {
            string detail = SanitizeFirstLine(run.StandardError);
            string message = $"O CLI do HyperFrames terminou com código {run.ExitCode}."
                             + (detail.Length > 0 ? " " + detail : string.Empty);
            return Failure(MotionRenderStatuses.Error, "RENDER_FAILED", message, manifestRelative, started);
        }

        if (!MotionPaths.IsCompleteOutputFile(context.ProjectRoot, context.OutputFileAbsolute))
        {
            return Failure(MotionRenderStatuses.Error, "OUTPUT_MISSING",
                "O CLI terminou sem gerar o arquivo de saída esperado. Formato de saída a confirmar no host (docs/20-HYPERFRAMES.md).",
                manifestRelative, started);
        }

        DateTime finished = _time.GetUtcNow().UtcDateTime;
        return new MotionRenderResultDto(
            Status: MotionRenderStatuses.ReadyReal,
            IsSimulated: false,
            OutputRelativePath: MotionPaths.OutputRelative(context.RenderId),
            ManifestRelativePath: manifestRelative,
            ErrorCode: null,
            ErrorMessage: null,
            StartedAtUtc: started,
            FinishedAtUtc: finished);
    }

    private async Task<Detection> DetectInternalAsync(CancellationToken ct)
    {
        DateTime now = _time.GetUtcNow().UtcDateTime;

        // 1) Node.js: sempre um caminho absoluto, para que o processo não seja procurado no diretório atual.
        bool nodeConfigured = _options.NodePath is not null;
        string? nodePath = ResolveNodePath(_options.NodePath, Environment.GetEnvironmentVariable("PATH"), File.Exists);
        if (nodePath is null)
        {
            return Unavailable(MotionEnvironmentStates.NodeUnavailable,
                nodeConfigured ? NodePathNotFoundCode : "NODE_NOT_FOUND",
                nodeConfigured
                    ? $"O caminho em {HyperFramesOptions.NodeEnvironmentVariable} deve ser um arquivo node existente, em caminho absoluto."
                    : $"Node.js não encontrado no PATH do host. Inclua o Node.js no PATH ou defina {HyperFramesOptions.NodeEnvironmentVariable} com o caminho absoluto do node. O HyperFrames exige Node.js.",
                nodeVersion: null, now);
        }

        var node = await _runner.RunAsync(
            new ProcessRunRequest(nodePath, new[] { VersionArgument }, _options.NodeCheckTimeout), ct).ConfigureAwait(false);

        if (node.Status == ProcessRunStatus.Canceled)
            throw new OperationCanceledException(ct);

        if (node.Status != ProcessRunStatus.Completed || node.ExitCode != 0)
        {
            string reason = node.Status == ProcessRunStatus.NotStarted ? "NODE_NOT_FOUND" : "NODE_CHECK_FAILED";
            return Unavailable(MotionEnvironmentStates.NodeUnavailable, reason,
                "Node.js não encontrado ou não respondeu a --version. O HyperFrames exige Node.js no PATH do host.",
                nodeVersion: null, now);
        }

        string? nodeVersion = ParseNodeVersion(node.StandardOutput);
        if (nodeVersion is null)
        {
            return Unavailable(MotionEnvironmentStates.NodeUnavailable, "NODE_VERSION_UNPARSEABLE",
                "Resposta inesperada de node --version.", nodeVersion: null, now);
        }

        // 2) CLI local apontado por variável de ambiente
        if (string.IsNullOrWhiteSpace(_options.CliPath))
        {
            return Unavailable(MotionEnvironmentStates.NotInstalled, "CLI_NOT_CONFIGURED",
                $"HyperFrames não configurado. Defina {HyperFramesOptions.CliEnvironmentVariable} com o caminho do CLI local (docs/20-HYPERFRAMES.md).",
                nodeVersion, now);
        }

        if (!Path.IsPathFullyQualified(_options.CliPath) || !File.Exists(_options.CliPath))
        {
            return Unavailable(MotionEnvironmentStates.NotInstalled, "CLI_PATH_NOT_FOUND",
                $"O caminho em {HyperFramesOptions.CliEnvironmentVariable} deve ser um arquivo existente.",
                nodeVersion, now);
        }

        string cliPath = _options.CliPath;

        if (IsUnsupportedWrapper(cliPath))
        {
            return CliError(UnsupportedCliTypeCode,
                $"O caminho em {HyperFramesOptions.CliEnvironmentVariable} é um script de shell (.cmd, .bat ou .ps1), que não é aceito. "
                + "Aponte para o .js do pacote do HyperFrames (executado pelo node) ou para um executável .exe.",
                nodeVersion, now, "CLI em formato não suportado. " + SupportedNote);
        }

        // 3) Versão do CLI (única leitura real de versão)
        var invocation = BuildInvocation(nodePath, cliPath, new[] { VersionArgument });
        var version = await _runner.RunAsync(
            new ProcessRunRequest(invocation.FileName, invocation.Arguments, _options.CliVersionTimeout), ct).ConfigureAwait(false);

        if (version.Status == ProcessRunStatus.Canceled)
            throw new OperationCanceledException(ct);

        if (version.Status == ProcessRunStatus.NotStarted)
            return CliError("CLI_NOT_EXECUTABLE", "O CLI configurado não pôde ser executado.", nodeVersion, now);

        if (version.Status == ProcessRunStatus.TimedOut)
            return CliError("CLI_VERSION_TIMEOUT", "O CLI não respondeu a --version dentro do tempo limite.", nodeVersion, now);

        if (version.ExitCode != 0)
            return CliError("CLI_VERSION_FAILED", $"O CLI respondeu a --version com código {version.ExitCode}.", nodeVersion, now);

        string? rendererVersion = ParseCliVersion(version.StandardOutput);
        if (rendererVersion is null)
            return CliError("CLI_VERSION_UNPARSEABLE", "Resposta inesperada do CLI a --version; formato a confirmar no host.", nodeVersion, now);

        var caps = new MotionCapabilitiesDto(
            SchemaVersion: MotionSchemaVersions.Capabilities,
            AdapterMode: ModeName,
            EnvironmentState: MotionEnvironmentStates.Ready,
            IsAvailable: true,
            IsSimulated: false,
            NodeVersion: nodeVersion,
            RendererVersion: rendererVersion,
            StatusMessage: "CLI do HyperFrames encontrado e versão lida. O formato de render ainda não foi verificado no host.",
            ReasonCode: null,
            Templates: MotionCapabilityTemplates.Build(supported: true, VerificationPending, SupportedNote),
            CheckedAtUtc: now);

        return new Detection(caps, cliPath, nodePath);
    }

    private Detection Unavailable(string state, string reason, string message, string? nodeVersion, DateTime now)
    {
        var caps = new MotionCapabilitiesDto(
            SchemaVersion: MotionSchemaVersions.Capabilities,
            AdapterMode: ModeName,
            EnvironmentState: state,
            IsAvailable: false,
            IsSimulated: false,
            NodeVersion: nodeVersion,
            RendererVersion: null,
            StatusMessage: message,
            ReasonCode: reason,
            Templates: MotionCapabilityTemplates.Build(supported: false, VerificationPending,
                "Indisponível neste ambiente. " + SupportedNote),
            CheckedAtUtc: now);
        return new Detection(caps, null, null);
    }

    private Detection CliError(string reason, string message, string? nodeVersion, DateTime now,
        string note = "CLI com erro na leitura de versão. ")
    {
        var caps = new MotionCapabilitiesDto(
            SchemaVersion: MotionSchemaVersions.Capabilities,
            AdapterMode: ModeName,
            EnvironmentState: MotionEnvironmentStates.Error,
            IsAvailable: false,
            IsSimulated: false,
            NodeVersion: nodeVersion,
            RendererVersion: null,
            StatusMessage: message,
            ReasonCode: reason,
            Templates: MotionCapabilityTemplates.Build(supported: false, VerificationPending, note + SupportedNote),
            CheckedAtUtc: now);
        return new Detection(caps, null, null);
    }

    private MotionRenderResultDto Failure(string status, string code, string message, string manifestRelative, DateTime started) =>
        new(
            Status: status,
            IsSimulated: false,
            OutputRelativePath: null,
            ManifestRelativePath: manifestRelative,
            ErrorCode: code,
            ErrorMessage: message,
            StartedAtUtc: started,
            FinishedAtUtc: _time.GetUtcNow().UtcDateTime);

    /// <summary>
    /// Nome do executável Node no PATH. No Windows o instalador oficial entrega <c>node.exe</c>; shims <c>.cmd</c> não são usados.
    /// </summary>
    public static string NodeFileName => OperatingSystem.IsWindows() ? NodeExecutable + ".exe" : NodeExecutable;

    /// <summary>
    /// Caminho absoluto do Node: o configurado (TRAVELCUT_NODE_PATH, arquivo existente e absoluto) ou o primeiro
    /// <see cref="NodeFileName"/> encontrado em entradas absolutas do PATH. Entradas relativas (inclusive ".", vazias
    /// e o diretório atual) são ignoradas. Nulo se não houver candidato.
    /// </summary>
    public static string? ResolveNodePath(string? configuredPath, string? pathValue, Func<string, bool> isFile)
    {
        ArgumentNullException.ThrowIfNull(isFile);

        if (configuredPath is not null)
            return IsAbsoluteExistingFile(configuredPath, isFile) ? configuredPath : null;

        return FindExecutableOnPath(NodeFileName, pathValue, isFile);
    }

    /// <summary>Procura <paramref name="fileName"/> em cada entrada absoluta de <paramref name="pathValue"/>, na ordem do PATH.</summary>
    public static string? FindExecutableOnPath(string fileName, string? pathValue, Func<string, bool> isFile)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
            return null;

        foreach (string rawEntry in pathValue.Split(Path.PathSeparator))
        {
            string entry = Environment.ExpandEnvironmentVariables(rawEntry.Trim().Trim('"')).Trim();
            if (entry.Length == 0 || !IsFullyQualified(entry))
                continue;

            string candidate;
            try
            {
                candidate = Path.Combine(entry, fileName);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (IsAbsoluteExistingFile(candidate, isFile))
                return candidate;
        }

        return null;
    }

    private static bool IsAbsoluteExistingFile(string path, Func<string, bool> isFile) =>
        IsFullyQualified(path) && isFile(path);

    private static bool IsFullyQualified(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    internal static bool IsUnsupportedWrapper(string cliPath) =>
        UnsupportedWrapperExtensions.Contains(Path.GetExtension(cliPath).ToLowerInvariant());

    /// <summary>
    /// Scripts JS rodam com "&lt;node absoluto&gt; &lt;script&gt;"; demais caminhos são executados diretamente. Sempre sem shell:
    /// caminhos com "&amp;", espaços ou parênteses chegam intactos em ArgumentList.
    /// </summary>
    internal static (string FileName, IReadOnlyList<string> Arguments) BuildInvocation(string nodePath, string cliPath, IReadOnlyList<string> arguments)
    {
        string ext = Path.GetExtension(cliPath).ToLowerInvariant();
        if (ScriptExtensions.Contains(ext))
        {
            var withScript = new List<string>(arguments.Count + 1) { cliPath };
            withScript.AddRange(arguments);
            return (nodePath, withScript);
        }

        return (cliPath, arguments);
    }

    internal static string? ParseNodeVersion(string? stdout)
    {
        string first = FirstNonEmptyLine(stdout);
        return NodeVersionPattern.IsMatch(first) ? first.TrimStart('v') : null;
    }

    public static string? ParseCliVersion(string? stdout)
    {
        string first = FirstNonEmptyLine(stdout);
        var match = CliVersionPattern.Match(first);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Primeira linha não vazia, sem espaços das extremidades. Vazio se não houver.</summary>
    internal static string FirstNonEmptyLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        return text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
    }

    /// <summary>Primeira linha de stderr, sem caminhos absolutos e truncada. Não expõe a saída completa.</summary>
    public static string SanitizeFirstLine(string? stderr)
    {
        string line = AbsolutePathPattern.Replace(FirstNonEmptyLine(stderr), "<caminho>");
        return line.Length > MaxMessageLength ? line[..MaxMessageLength] : line;
    }

    private sealed record Detection(MotionCapabilitiesDto Capabilities, string? CliPath, string? NodePath);
}
