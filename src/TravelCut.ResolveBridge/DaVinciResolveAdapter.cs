using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;

namespace TravelCut.ResolveBridge;

/// <summary>
/// Adaptador real do DaVinci Resolve (contrato editor/1). Conversa com workers/resolve_bridge.py via
/// <see cref="IProcessRunner"/> (stdin/stdout JSON, protocolo "resolve-bridge/1").
/// Regras:
/// - Sem fallback silencioso para mock: ambiente indisponível devolve estado explícito.
/// - Nenhum sucesso é afirmado sem resposta real da bridge (READY_REAL/PARTIAL_REAL vêm do bridge).
/// - Só cria/escreve em projeto novo com prefixo TravelCut_; nunca abre projeto existente para escrita.
/// - Mensagens não contêm caminhos absolutos (são substituídos por "&lt;caminho&gt;").
/// Estado: capacidades "host_pending" até homologação no host (docs/18-ROTEIRO-HOMOLOGACAO-DAVINCI.md).
/// </summary>
public sealed class DaVinciResolveAdapter : IEditorAdapter
{
    public const string BridgeSchemaVersion = "resolve-bridge/1";
    public const string ManifestSchemaVersion = "editor-assembly/1";
    public const string ProjectPrefix = "TravelCut_";
    public const string ModeName = "davinci";

    /// <summary>Métodos verificados pelo probe do bridge (hasattr).</summary>
    public static readonly IReadOnlyList<string> ProbedMethods = new[]
    {
        "CreateProject", "ImportMedia", "CreateEmptyTimeline", "AppendToTimeline",
        "GetItemListInTrack", "ExportProject", "AddRenderJob", "SetSetting",
        "SaveProject", "SetCurrentTimeline", "GetMediaPool", "SetRenderSettings",
        "StartRendering", "IsRenderingInProgress", "SetCurrentRenderFormatAndCodec",
    };

    private static readonly string[] AllOperations =
    {
        EditorOperations.DetectCapabilities,
        EditorOperations.CreateProject,
        EditorOperations.ImportMedia,
        EditorOperations.CreateTimeline,
        EditorOperations.ApplyEditPlan,
        EditorOperations.QueryTimeline,
        EditorOperations.ExportPreview,
        EditorOperations.ExportProject,
        EditorOperations.CreateSnapshot,
    };

    // Métodos da API exigidos por operação (apply precisa da cadeia completa).
    private static readonly IReadOnlyDictionary<string, string[]> OperationRequirements =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [EditorOperations.DetectCapabilities] = Array.Empty<string>(),
            [EditorOperations.CreateProject] = new[] { "CreateProject" },
            [EditorOperations.ImportMedia] = new[] { "ImportMedia" },
            [EditorOperations.CreateTimeline] = new[] { "CreateEmptyTimeline" },
            [EditorOperations.ApplyEditPlan] = new[]
            {
                "CreateProject", "SetSetting", "ImportMedia", "CreateEmptyTimeline", "AppendToTimeline",
                "GetItemListInTrack", "SaveProject", "SetCurrentTimeline", "GetMediaPool",
            },
            [EditorOperations.QueryTimeline] = new[] { "GetItemListInTrack" },
            [EditorOperations.ExportPreview] = new[] { "SetCurrentTimeline", "SetRenderSettings", "AddRenderJob", "StartRendering", "IsRenderingInProgress" },
            [EditorOperations.ExportProject] = new[] { "ExportProject" },
            [EditorOperations.CreateSnapshot] = new[] { "ExportProject" },
        };

    private static readonly IReadOnlyDictionary<string, bool?> EmptyMethods =
        new Dictionary<string, bool?>(StringComparer.Ordinal);

    private static readonly TimeSpan DetectionCacheTtl = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PythonCheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ExportProjectTimeout = TimeSpan.FromMinutes(30);
    private const int RenderTimeoutSec = 1800;
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(RenderTimeoutSec) + TimeSpan.FromMinutes(1);

    private const string PythonCheckCode =
        "import sys,struct; print('TRAVELCUT_PY=' + '.'.join(str(v) for v in sys.version_info[:3]) + ' bits=' + str(struct.calcsize('P')*8))";
    private static readonly Regex PythonMarker =
        new(@"TRAVELCUT_PY=(\d+)\.(\d+)\.(\d+) bits=(\d+)", RegexOptions.CultureInvariant);

    private static readonly char[] ForbiddenNameChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };

    private static readonly IReadOnlyDictionary<string, string> PythonEnvironment =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PYTHONUTF8"] = "1",
            ["PYTHONIOENCODING"] = "utf-8",
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    /// <summary>Código estável para "editor indisponível" em resultados (o estado específico vai na mensagem).</summary>
    public const string EditorUnavailableCode = "EDITOR_UNAVAILABLE";

    /// <summary>Código de detecção quando o Resolve está ocupado com outra operação (sem sonda).</summary>
    public const string EditorBusyCode = "EDITOR_BUSY";

    private const string BusySuffix = " (operação em andamento no Resolve; estado da última verificação)";
    private const string BusyReasonCode = EditorBusyCode;
    private const string NotInstalledPathMessage =
        "DaVinci Resolve não encontrado no caminho configurado. Defina TRAVELCUT_RESOLVE_PATH ou instale o DaVinci Resolve Studio.";
    private const string NotRunningMessage =
        "DaVinci Resolve está instalado, mas não está em execução. Abra o Resolve Studio e habilite o scripting externo local.";

    /// <summary>Segmento que identifica a área de artefatos do editor: raiz permitida = &lt;projeto&gt;\3-Trabalho\editor.</summary>
    private static readonly string ArtifactMarker =
        Path.DirectorySeparatorChar + "3-Trabalho" + Path.DirectorySeparatorChar + "editor" + Path.DirectorySeparatorChar;

    private static readonly string ArtifactFolderSuffix =
        Path.DirectorySeparatorChar + "3-Trabalho" + Path.DirectorySeparatorChar + "editor";

    private readonly IProcessRunner _runner;
    private readonly ResolveBridgeOptions _options;
    private readonly ILogger<DaVinciResolveAdapter> _logger;
    private readonly Func<bool> _isResolveRunning;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);          // detecção (probe)
    private readonly SemaphoreSlim _resolveGate = new(1, 1);   // apply/query/export/render: um por vez no Resolve
    private Detection? _cache;

    private sealed record PythonRuntime(string FileName, IReadOnlyList<string> PrefixArgs, string Version);

    private sealed record Detection(EditorCapabilitiesDto Capabilities, PythonRuntime? Python, DateTime AtUtc);

    private sealed record BridgeReply(JsonObject? Root, string? ErrorCode, string? ErrorMessage);

    /// <param name="isResolveRunning">Verificação de processo; null = processo "Resolve" do sistema (injetável em teste).</param>
    /// <param name="timeProvider">Relógio (cache de detecção e instantes de capacidades); null = <see cref="TimeProvider.System"/>.</param>
    public DaVinciResolveAdapter(
        IProcessRunner runner,
        ResolveBridgeOptions options,
        ILogger<DaVinciResolveAdapter> logger,
        Func<bool>? isResolveRunning = null,
        TimeProvider? timeProvider = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isResolveRunning = isResolveRunning ?? new Func<bool>(IsResolveProcessRunning);
        _time = timeProvider ?? TimeProvider.System;
    }

    public string Mode => ModeName;

    /// <summary>
    /// Raiz permitida (&lt;projeto&gt;\3-Trabalho\editor) derivada do próprio caminho, já que <see cref="IEditorAdapter"/> recebe só
    /// caminhos. Exige o segmento "\3-Trabalho\editor\" (última ocorrência, após Path.GetFullPath) e usa o prefixo até ele.
    /// null = caminho fora da área de artefatos do editor.
    /// </summary>
    public static string? DeriveAllowedOutputRoot(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathFullyQualified(absolutePath))
        {
            return null;
        }

        string full;
        try
        {
            full = Path.GetFullPath(absolutePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var index = full.LastIndexOf(ArtifactMarker, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? null : full[..index] + ArtifactFolderSuffix;
    }

    /// <summary>Nome de projeto aceito para escrita/leitura pelo TravelCut.</summary>
    public static bool IsTravelCutProjectName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.StartsWith(ProjectPrefix, StringComparison.Ordinal)
        && name.Length > ProjectPrefix.Length
        && name.Length <= 160
        && name.IndexOfAny(ForbiddenNameChars) < 0
        && !name.Any(char.IsControl);

    // ---- Detecção ----

    public async Task<EditorCapabilitiesDto> DetectCapabilitiesAsync(CancellationToken ct = default)
    {
        // Enquanto apply/query/export/render usa o Resolve (_resolveGate ocupado), NÃO sonda: a sonda tocaria o
        // Resolve no meio da operação. Tentativa sem espera; ocupado -> último estado conhecido, marcado.
        if (!await _resolveGate.WaitAsync(TimeSpan.Zero, ct).ConfigureAwait(false))
        {
            return BusyCapabilities();
        }

        try
        {
            var detection = await GetDetectionAsync(ct).ConfigureAwait(false);
            return detection.Capabilities;
        }
        finally
        {
            _resolveGate.Release();
        }
    }

    /// <summary>Estado devolvido quando o Resolve está ocupado: cache marcado, ou pré-check sem sonda.</summary>
    private EditorCapabilitiesDto BusyCapabilities()
    {
        var cached = _cache;
        if (cached is not null)
        {
            return cached.Capabilities with { StatusMessage = cached.Capabilities.StatusMessage + BusySuffix };
        }

        // Sem cache: só o pré-check de processo/instalação. Estado nunca READY aqui, então isAvailable fica false.
        var (state, message) = ProcessPreCheck();
        return Build(state, message + BusySuffix, BusyReasonCode, null, null, EmptyMethods, null).Capabilities;
    }

    /// <summary>Pré-check sem sonda: instalação e processo. Quando ambos passam, o estado real depende da sonda.</summary>
    private (string State, string Message) ProcessPreCheck()
    {
        var resolvePath = _options.ResolveExecutablePath;
        if (resolvePath is not null && !File.Exists(resolvePath))
        {
            return (EditorEnvironmentStates.NotInstalled, NotInstalledPathMessage);
        }

        if (!_isResolveRunning())
        {
            return (EditorEnvironmentStates.NotRunning, NotRunningMessage);
        }

        return (EditorEnvironmentStates.Error, "Resolve em uso por outra operação; o estado será verificado quando ela terminar.");
    }

    private async Task<Detection> GetDetectionAsync(CancellationToken ct)
    {
        var cached = _cache;
        if (cached is not null && _time.GetUtcNow().UtcDateTime - cached.AtUtc < DetectionCacheTtl)
        {
            return cached;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            cached = _cache;
            if (cached is not null && _time.GetUtcNow().UtcDateTime - cached.AtUtc < DetectionCacheTtl)
            {
                return cached;
            }

            var fresh = await DetectCoreAsync(ct).ConfigureAwait(false);
            _cache = fresh;
            _logger.LogInformation("Detecção DaVinci: estado {State} (motivo {Reason})",
                fresh.Capabilities.EnvironmentState, fresh.Capabilities.ReasonCode ?? "-");
            return fresh;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Detection> DetectCoreAsync(CancellationToken ct)
    {
        var resolvePath = _options.ResolveExecutablePath;
        if (resolvePath is not null && !File.Exists(resolvePath))
        {
            return Build(EditorEnvironmentStates.NotInstalled, NotInstalledPathMessage,
                EditorEnvironmentStates.NotInstalled, null, null, EmptyMethods, null);
        }

        if (!_isResolveRunning())
        {
            return Build(EditorEnvironmentStates.NotRunning, NotRunningMessage,
                EditorEnvironmentStates.NotRunning, null, null, EmptyMethods, null);
        }

        var (python, pythonReason) = await DiscoverPythonAsync(ct).ConfigureAwait(false);
        if (python is null)
        {
            return Build(EditorEnvironmentStates.PythonUnavailable, pythonReason,
                EditorEnvironmentStates.PythonUnavailable, null, null, EmptyMethods, null);
        }

        if (!File.Exists(_options.BridgeScriptPath))
        {
            return Build(EditorEnvironmentStates.Error,
                "Script do bridge (workers/resolve_bridge.py) não encontrado ao lado do aplicativo.",
                "BRIDGE_SCRIPT_MISSING", null, null, EmptyMethods, python);
        }

        var probe = await CallBridgeAsync(python, _options.BridgeScriptPath, "probe",
            new Dictionary<string, object?>(), _options.ProbeTimeout, ct).ConfigureAwait(false);

        if (probe.ErrorCode is { } transportCode)
        {
            var transportState = transportCode == EditorEnvironmentStates.PythonUnavailable
                ? EditorEnvironmentStates.PythonUnavailable
                : EditorEnvironmentStates.Error;
            return Build(transportState, probe.ErrorMessage ?? "Falha ao consultar o bridge.",
                transportCode, null, null, EmptyMethods, python);
        }

        var root = probe.Root!;
        if (!GetBool(root, "ok"))
        {
            var code = GetString(root, "errorCode") ?? "BRIDGE_FAILED";
            var message = Sanitize(GetString(root, "errorMessage"), SecretPaths()) ?? "Bridge recusou o probe.";
            var state = code switch
            {
                "SCRIPTING_DISABLED" => EditorEnvironmentStates.ScriptingDisabled,
                "NOT_INSTALLED" => EditorEnvironmentStates.NotInstalled,
                _ => EditorEnvironmentStates.Error,
            };
            return Build(state, message, code, null, null, EmptyMethods, python);
        }

        var methods = ReadMethods(root);
        var productName = Sanitize(GetString(root, "productName"), SecretPaths());
        var version = Sanitize(GetString(root, "version"), SecretPaths());
        var missing = methods.Where(kv => kv.Value == false).Select(kv => kv.Key).ToList();
        var unknown = methods.Where(kv => kv.Value is null).Select(kv => kv.Key).ToList();

        // Método null (não verificável sem projeto aberto) NÃO conta como presente: o ambiente fica READY_PARTIAL.
        if (missing.Count == 0 && unknown.Count == 0)
        {
            return Build(EditorEnvironmentStates.Ready,
                "DaVinci Resolve pronto para o bridge; operações ainda não homologadas no host.",
                null, productName, version, methods, python);
        }

        if (missing.Count == 0)
        {
            return Build(EditorEnvironmentStates.ReadyPartial,
                "Operações não verificáveis sem projeto aberto: " + string.Join(", ", unknown)
                + ". Abra qualquer projeto no Resolve para verificar.",
                "METHODS_UNVERIFIED", productName, version, methods, python);
        }

        var partialMessage = "Operações parciais: métodos ausentes nesta versão: " + string.Join(", ", missing) + ".";
        if (unknown.Count > 0)
        {
            partialMessage += " Não verificáveis sem projeto aberto: " + string.Join(", ", unknown) + ".";
        }
        return Build(EditorEnvironmentStates.ReadyPartial, partialMessage, "METHODS_MISSING",
            productName, version, methods, python);
    }

    private Detection Build(
        string state,
        string message,
        string? reasonCode,
        string? productName,
        string? version,
        IReadOnlyDictionary<string, bool?> methods,
        PythonRuntime? python)
    {
        var ready = state is EditorEnvironmentStates.Ready or EditorEnvironmentStates.ReadyPartial;
        var operations = BuildOperations(ready, methods, ready ? null : message);

        bool Supported(string operation) => operations.Any(o => o.Operation == operation && o.Supported);

        var capabilities = new EditorCapabilitiesDto(
            SchemaVersion: "editor/1",
            AdapterMode: ModeName,
            EnvironmentState: state,
            IsAvailable: ready,
            IsSimulated: false,
            ProductName: productName,
            Version: version,
            StatusMessage: message,
            ReasonCode: reasonCode,
            Operations: operations,
            CheckedAtUtc: _time.GetUtcNow().UtcDateTime,
            CanCreateProject: Supported(EditorOperations.CreateProject),
            CanImportMedia: Supported(EditorOperations.ImportMedia),
            CanCreateTimeline: Supported(EditorOperations.CreateTimeline),
            CanApplyEditPlan: Supported(EditorOperations.ApplyEditPlan),
            CanExportPreview: Supported(EditorOperations.ExportPreview));

        return new Detection(capabilities, python, _time.GetUtcNow().UtcDateTime);
    }

    private static List<EditorOperationCapabilityDto> BuildOperations(
        bool ready,
        IReadOnlyDictionary<string, bool?> methods,
        string? notReadyNote)
    {
        var list = new List<EditorOperationCapabilityDto>(AllOperations.Length);
        foreach (var operation in AllOperations)
        {
            if (!ready)
            {
                list.Add(new EditorOperationCapabilityDto(operation, false, CapabilityVerification.HostPending, notReadyNote));
                continue;
            }

            if (operation == EditorOperations.DetectCapabilities)
            {
                list.Add(new EditorOperationCapabilityDto(operation, true, CapabilityVerification.HostPending,
                    "Probe do bridge executado; ainda não homologado no host."));
                continue;
            }

            var required = OperationRequirements[operation];
            var missing = required.Where(m => methods.TryGetValue(m, out var v) && v == false).ToList();
            if (missing.Count > 0)
            {
                list.Add(new EditorOperationCapabilityDto(operation, false, CapabilityVerification.Unsupported,
                    "Método ausente nesta versão do Resolve: " + string.Join(", ", missing) + "."));
                continue;
            }

            // Método não verificável (null) não habilita a operação: só método confirmado pelo probe (true) habilita.
            var unknown = required.Where(m => !methods.TryGetValue(m, out var v) || v is null).ToList();
            if (unknown.Count > 0)
            {
                list.Add(new EditorOperationCapabilityDto(operation, false, CapabilityVerification.HostPending,
                    "Método(s) não verificáveis sem projeto aberto: " + string.Join(", ", unknown)
                    + "; abra um projeto no Resolve para verificar."));
                continue;
            }
            list.Add(new EditorOperationCapabilityDto(operation, true, CapabilityVerification.HostPending,
                "Método(s) presentes; operação ainda não homologada no host."));
        }
        return list;
    }

    private static Dictionary<string, bool?> ReadMethods(JsonObject root)
    {
        var result = new Dictionary<string, bool?>(StringComparer.Ordinal);
        var node = root["methods"] as JsonObject;
        foreach (var name in ProbedMethods)
        {
            bool? value = null;
            if (node is not null
                && node.TryGetPropertyValue(name, out var raw)
                && raw is JsonValue jsonValue
                && jsonValue.TryGetValue<bool>(out var flag))
            {
                value = flag;
            }
            result[name] = value;
        }
        return result;
    }

    // ---- Python ----

    private async Task<(PythonRuntime? Runtime, string Reason)> DiscoverPythonAsync(CancellationToken ct)
    {
        var candidates = new List<(string Exe, string[] Prefix)>();
        if (!string.IsNullOrWhiteSpace(_options.PythonExecutable))
        {
            candidates.Add((_options.PythonExecutable!, Array.Empty<string>()));
        }
        else
        {
            candidates.Add(("py", new[] { "-3" }));
            candidates.Add(("python", Array.Empty<string>()));
        }

        var reason = "Python 3 (64 bits) não encontrado. Defina TRAVELCUT_PYTHON com o python.exe compatível com o DaVinci Resolve.";
        foreach (var (exe, prefix) in candidates)
        {
            var args = prefix.Concat(new[] { "-c", PythonCheckCode }).ToArray();
            var run = await _runner.RunAsync(new ProcessRunRequest(exe, args, PythonCheckTimeout), ct).ConfigureAwait(false);
            if (run.Status == ProcessRunStatus.Canceled)
            {
                throw new OperationCanceledException(ct);
            }
            if (run.Status != ProcessRunStatus.Completed || run.ExitCode != 0)
            {
                continue;
            }

            var match = PythonMarker.Match(run.StandardOutput ?? string.Empty);
            if (!match.Success)
            {
                continue;
            }

            if (match.Groups[4].Value != "64")
            {
                reason = "Python de 32 bits detectado; o DaVinci Resolve requer Python 64 bits.";
                continue;
            }

            var major = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var minor = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (major < 3 || (major == 3 && minor < 6))
            {
                reason = "Python 3.6 ou superior é necessário para o bridge do DaVinci.";
                continue;
            }

            var version = $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}";
            return (new PythonRuntime(exe, prefix, version), string.Empty);
        }

        return (null, reason);
    }

    // ---- Chamada à bridge ----

    private async Task<BridgeReply> CallBridgeAsync(
        PythonRuntime python,
        string scriptPath,
        string command,
        Dictionary<string, object?> fields,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var body = new Dictionary<string, object?>(fields, StringComparer.Ordinal)
        {
            ["schemaVersion"] = BridgeSchemaVersion,
            ["command"] = command,
        };
        var stdin = JsonSerializer.Serialize(body, JsonOptions) + "\n";
        var args = python.PrefixArgs.Append(scriptPath).ToArray();

        var run = await _runner.RunAsync(
            new ProcessRunRequest(python.FileName, args, timeout, stdin, PythonEnvironment), ct).ConfigureAwait(false);

        switch (run.Status)
        {
            case ProcessRunStatus.Canceled:
                throw new OperationCanceledException(ct);
            case ProcessRunStatus.NotStarted:
                return new BridgeReply(null, EditorEnvironmentStates.PythonUnavailable,
                    "Python não pôde ser iniciado para o bridge do DaVinci.");
            case ProcessRunStatus.TimedOut:
                return new BridgeReply(null, "TIMEOUT",
                    "Tempo esgotado aguardando o bridge do DaVinci (" + command + ").");
        }

        var root = ParseLastJsonLine(run.StandardOutput);
        if (root is null)
        {
            return run.ExitCode is { } exit && exit != 0
                ? new BridgeReply(null, "BRIDGE_FAILED", "Bridge terminou com código " + exit + " sem resposta válida.")
                : new BridgeReply(null, "BRIDGE_PROTOCOL_ERROR", "Resposta do bridge vazia ou inválida.");
        }

        if (GetString(root, "schemaVersion") != BridgeSchemaVersion)
        {
            return new BridgeReply(null, "BRIDGE_PROTOCOL_ERROR", "Versão de protocolo do bridge não reconhecida.");
        }

        return new BridgeReply(root, null, null);
    }

    /// <summary>
    /// Chamada que toca o Resolve (apply, query, export, render). Serializada por <see cref="_resolveGate"/>: duas chamadas
    /// nunca se sobrepõem. Espera cancelável. A detecção usa o próprio gate (<see cref="_gate"/>).
    /// </summary>
    private async Task<BridgeReply> CallResolveAsync(
        PythonRuntime python,
        string command,
        Dictionary<string, object?> fields,
        TimeSpan timeout,
        CancellationToken ct)
    {
        await _resolveGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await CallBridgeAsync(python, _options.BridgeScriptPath, command, fields, timeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _resolveGate.Release();
        }
    }

    /// <summary>Indisponibilidade do Python/bridge vira EDITOR_UNAVAILABLE (o detalhe fica na mensagem).</summary>
    private static string MapTransportCode(string code) =>
        code == EditorEnvironmentStates.PythonUnavailable ? EditorUnavailableCode : code;

    /// <summary>Usa a última linha JSON do stdout (tolera ruído de bibliotecas nativas antes da resposta).</summary>
    private static JsonObject? ParseLastJsonLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] != '{')
            {
                continue;
            }
            try
            {
                return JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }
        return null;
    }

    // ---- Apply ----

    public async Task<EditorAdapterApplyResult> ApplyAssemblyAsync(
        EditorAssemblyDto assembly,
        EditorApplyOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(options);

        if (assembly.SchemaVersion != ManifestSchemaVersion)
        {
            return Fail(EditorResultStatus.Error, "CONTRACT_VERSION_MISMATCH", "Manifesto com schemaVersion não suportado.");
        }
        if (!IsTravelCutProjectName(assembly.EditorProjectName))
        {
            return Fail(EditorResultStatus.Error, "PERMISSION_DENIED", "Nome de projeto sem prefixo TravelCut_ ou inválido.");
        }
        if (assembly.Clips.Count == 0)
        {
            return Fail(EditorResultStatus.Error, "NO_CLIPS", "Manifesto sem clipes.");
        }
        if (options.SnapshotAbsolutePath is { } requestedSnapshot && !Path.IsPathFullyQualified(requestedSnapshot))
        {
            return Fail(EditorResultStatus.Error, "INVALID_PATH", "Caminho de snapshot deve ser absoluto.");
        }

        // Confinamento antes de qualquer acesso ao Resolve: manifesto e snapshot só em <projeto>\3-Trabalho\editor,
        // e do mesmo projeto. A raiz derivada também segue para a bridge (allowedOutputRoot).
        string? manifestRoot = null;
        if (options.ManifestAbsolutePath is { } requestedManifest)
        {
            manifestRoot = DeriveAllowedOutputRoot(requestedManifest);
            if (manifestRoot is null)
            {
                return Fail(EditorResultStatus.Error, "PERMISSION_DENIED", "Manifesto fora de 3-Trabalho\\editor.");
            }
        }

        string? snapshotRoot = null;
        if (options.SnapshotAbsolutePath is not null)
        {
            snapshotRoot = DeriveAllowedOutputRoot(options.SnapshotAbsolutePath);
            if (snapshotRoot is null)
            {
                return Fail(EditorResultStatus.Error, "PERMISSION_DENIED", "Snapshot fora de 3-Trabalho\\editor.");
            }
        }

        if (manifestRoot is not null && snapshotRoot is not null
            && !string.Equals(manifestRoot, snapshotRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Fail(EditorResultStatus.Error, "PERMISSION_DENIED", "Snapshot e manifesto pertencem a projetos diferentes.");
        }

        var allowedRoot = manifestRoot ?? snapshotRoot;

        // Detecção ANTES de qualquer _resolveGate: a chamada ao Resolve abaixo adquire o gate sem aninhamento.
        var detection = await GetDetectionAsync(ct).ConfigureAwait(false);
        var unavailable = RequireOperation(detection, EditorOperations.ApplyEditPlan);
        if (unavailable is not null)
        {
            return Fail(unavailable.Value.Status, unavailable.Value.Code, unavailable.Value.Message);
        }

        var python = detection.Python!;
        var snapshotPath = options.SnapshotAbsolutePath;

        // Manifesto já gravado pelo use case (3-Trabalho/editor) é usado sem cópia; o temporário só existe no legado (null).
        var ownsManifest = options.ManifestAbsolutePath is null;
        var manifestPath = options.ManifestAbsolutePath
            ?? Path.Combine(Path.GetTempPath(), "TravelCut", "bridge", Guid.NewGuid().ToString("N") + ".json");

        try
        {
            if (ownsManifest)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
                await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(assembly, JsonOptions),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct).ConfigureAwait(false);
            }

            var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["manifestPath"] = manifestPath,
                ["endFrameInclusive"] = _options.EndFrameInclusive,
            };
            if (snapshotPath is not null)
            {
                fields["snapshotPath"] = snapshotPath;
            }
            if (allowedRoot is not null)
            {
                fields["allowedOutputRoot"] = allowedRoot;
            }

            var secrets = SecretPaths(manifestPath, snapshotPath);
            var reply = await CallResolveAsync(python, "apply_assembly", fields, options.Timeout, ct).ConfigureAwait(false);

            if (reply.ErrorCode is { } transportCode)
            {
                var code = MapTransportCode(transportCode);
                var transportStatus = code == EditorUnavailableCode ? EditorResultStatus.Unavailable : EditorResultStatus.Error;
                return Fail(transportStatus, code, Sanitize(reply.ErrorMessage, secrets) ?? "Falha no bridge.");
            }

            return MapApplyResponse(reply.Root!, assembly, snapshotPath, secrets);
        }
        finally
        {
            if (ownsManifest)
            {
                TryDelete(manifestPath);
            }
        }
    }

    private static EditorAdapterApplyResult MapApplyResponse(JsonObject root, EditorAssemblyDto assembly, string? snapshotPath, string?[] secrets)
    {
        var status = GetString(root, "status");
        if (status is not (EditorResultStatus.ReadyReal or EditorResultStatus.PartialReal))
        {
            status = EditorResultStatus.Error;
        }

        var steps = ReadSteps(root, secrets);
        var mappings = ReadMappings(root, assembly);
        var rejected = ReadRejected(root, secrets);
        var warnings = ReadWarnings(root, secrets);

        // Snapshot só é reportado se o bridge confirmou a etapa e o arquivo existe.
        string? snapshotAbsolute = null;
        if (snapshotPath is not null
            && steps.Any(s => s.Operation == "create_snapshot" && s.Status == "ok")
            && File.Exists(snapshotPath))
        {
            snapshotAbsolute = snapshotPath;
        }

        string? errorCode = null;
        string? errorMessage = null;
        if (status == EditorResultStatus.Error)
        {
            errorCode = GetString(root, "errorCode") ?? "BRIDGE_FAILED";
            errorMessage = Sanitize(GetString(root, "errorMessage"), secrets) ?? "Falha ao aplicar o manifesto.";
        }

        return new EditorAdapterApplyResult(
            status,
            Sanitize(GetString(root, "editorVersion"), secrets),
            steps,
            mappings,
            rejected,
            warnings,
            snapshotAbsolute,
            errorCode,
            errorMessage);
    }

    // ---- Timeline / exportação ----

    public async Task<EditorTimelineQueryDto> QueryTimelineAsync(string editorProjectName, string timelineName, CancellationToken ct = default)
    {
        if (!IsTravelCutProjectName(editorProjectName))
        {
            return TimelineFail(editorProjectName, timelineName, EditorResultStatus.Error,
                "PERMISSION_DENIED", "Somente projetos com prefixo TravelCut_ podem ser consultados.");
        }

        var detection = await GetDetectionAsync(ct).ConfigureAwait(false);
        var unavailable = RequireOperation(detection, EditorOperations.QueryTimeline);
        if (unavailable is not null)
        {
            return TimelineFail(editorProjectName, timelineName,
                unavailable.Value.Status, unavailable.Value.Code, unavailable.Value.Message);
        }

        var secrets = SecretPaths();
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["projectName"] = editorProjectName,
            ["timelineName"] = timelineName,
        };
        var reply = await CallResolveAsync(detection.Python!, "query_timeline", fields, QueryTimeout, ct).ConfigureAwait(false);

        if (reply.ErrorCode is { } transportCode)
        {
            var code = MapTransportCode(transportCode);
            return TimelineFail(editorProjectName, timelineName,
                code == EditorUnavailableCode ? EditorResultStatus.Unavailable : EditorResultStatus.Error, code,
                Sanitize(reply.ErrorMessage, secrets) ?? "Falha na consulta da timeline.");
        }

        var root = reply.Root!;
        if (!GetBool(root, "ok"))
        {
            return TimelineFail(editorProjectName, timelineName, EditorResultStatus.Error,
                GetString(root, "errorCode") ?? "BRIDGE_FAILED",
                Sanitize(GetString(root, "errorMessage"), secrets) ?? "Consulta recusada pelo bridge.");
        }

        var items = new List<EditorTimelineItemDto>();
        if (root["items"] is JsonArray array)
        {
            foreach (var node in array)
            {
                if (node is not JsonObject item)
                {
                    continue;
                }
                var recordFrame = GetLong(item, "recordFrame");
                var duration = GetLong(item, "durationFrames");
                if (recordFrame is null || duration is null)
                {
                    continue;
                }
                var track = GetLong(item, "trackIndex") ?? 1;
                items.Add(new EditorTimelineItemDto(
                    GetString(item, "editorItemId"),
                    (int)track,
                    recordFrame.Value,
                    duration.Value,
                    SourceStartFrame: GetLong(item, "leftOffsetFrames"),   // in-point observado; null se a API não expôs
                    MediaName: GetString(item, "mediaName")));
            }
        }

        return new EditorTimelineQueryDto(
            EditorResultStatus.ReadyReal,
            editorProjectName,
            timelineName,
            GetLong(root, "timelineStartFrame"),
            items,
            null,
            null);
    }

    public async Task<EditorExportResultDto> ExportProjectAsync(string editorProjectName, string outputAbsolutePath, CancellationToken ct = default)
    {
        if (!IsTravelCutProjectName(editorProjectName))
        {
            return ExportFail(EditorResultStatus.Error, "PERMISSION_DENIED", "Somente projetos com prefixo TravelCut_ podem ser exportados.");
        }
        if (string.IsNullOrWhiteSpace(outputAbsolutePath) || !Path.IsPathFullyQualified(outputAbsolutePath))
        {
            return ExportFail(EditorResultStatus.Error, "INVALID_PATH", "Caminho de exportação deve ser absoluto.");
        }

        var allowedRoot = DeriveAllowedOutputRoot(outputAbsolutePath);
        if (allowedRoot is null)
        {
            return ExportFail(EditorResultStatus.Error, "PERMISSION_DENIED", "Exportação fora de 3-Trabalho\\editor.");
        }

        var detection = await GetDetectionAsync(ct).ConfigureAwait(false);
        var unavailable = RequireOperation(detection, EditorOperations.ExportProject);
        if (unavailable is not null)
        {
            return ExportFail(unavailable.Value.Status, unavailable.Value.Code, unavailable.Value.Message);
        }

        var secrets = SecretPaths(outputAbsolutePath);
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["projectName"] = editorProjectName,
            ["outputPath"] = outputAbsolutePath,
            ["allowedOutputRoot"] = allowedRoot,
        };
        var reply = await CallResolveAsync(detection.Python!, "export_project", fields, ExportProjectTimeout, ct).ConfigureAwait(false);

        if (reply.ErrorCode is { } transportCode)
        {
            var code = MapTransportCode(transportCode);
            return ExportFail(code == EditorUnavailableCode ? EditorResultStatus.Unavailable : EditorResultStatus.Error, code,
                Sanitize(reply.ErrorMessage, secrets) ?? "Falha na exportação.");
        }

        var root = reply.Root!;
        if (!GetBool(root, "ok"))
        {
            return ExportFail(EditorResultStatus.Error,
                GetString(root, "errorCode") ?? "EXPORT_FAILED",
                Sanitize(GetString(root, "errorMessage"), secrets) ?? "Exportação recusada pelo bridge.");
        }

        // Só afirma sucesso se o arquivo realmente existe no destino.
        if (!File.Exists(outputAbsolutePath))
        {
            return ExportFail(EditorResultStatus.Error, "EXPORT_FAILED", "Arquivo exportado não foi encontrado no destino.");
        }

        return new EditorExportResultDto(EditorResultStatus.ReadyReal, outputAbsolutePath, null, null);
    }

    public async Task<EditorExportResultDto> ExportPreviewAsync(string editorProjectName, string timelineName, string outputDirectoryAbsolute, CancellationToken ct = default)
    {
        if (!IsTravelCutProjectName(editorProjectName))
        {
            return ExportFail(EditorResultStatus.Error, "PERMISSION_DENIED", "Somente projetos com prefixo TravelCut_ podem ser renderizados.");
        }
        if (string.IsNullOrWhiteSpace(outputDirectoryAbsolute) || !Path.IsPathFullyQualified(outputDirectoryAbsolute))
        {
            return ExportFail(EditorResultStatus.Error, "INVALID_PATH", "Diretório de saída deve ser absoluto.");
        }

        var allowedRoot = DeriveAllowedOutputRoot(outputDirectoryAbsolute);
        if (allowedRoot is null)
        {
            return ExportFail(EditorResultStatus.Error, "PERMISSION_DENIED", "Diretório de render fora de 3-Trabalho\\editor.");
        }

        var detection = await GetDetectionAsync(ct).ConfigureAwait(false);
        var unavailable = RequireOperation(detection, EditorOperations.ExportPreview);
        if (unavailable is not null)
        {
            return ExportFail(unavailable.Value.Status, unavailable.Value.Code, unavailable.Value.Message);
        }

        var secrets = SecretPaths(outputDirectoryAbsolute);
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["projectName"] = editorProjectName,
            ["timelineName"] = timelineName,
            ["outputDir"] = outputDirectoryAbsolute,
            ["allowedOutputRoot"] = allowedRoot,
            ["timeoutSec"] = RenderTimeoutSec,
        };
        var reply = await CallResolveAsync(detection.Python!, "render_preview", fields, RenderTimeout, ct).ConfigureAwait(false);

        if (reply.ErrorCode is { } transportCode)
        {
            var code = MapTransportCode(transportCode);
            return ExportFail(code == EditorUnavailableCode ? EditorResultStatus.Unavailable : EditorResultStatus.Error, code,
                Sanitize(reply.ErrorMessage, secrets) ?? "Falha no render de prévia.");
        }

        var root = reply.Root!;
        if (!GetBool(root, "ok"))
        {
            return ExportFail(EditorResultStatus.Error,
                GetString(root, "errorCode") ?? "RENDER_FAILED",
                Sanitize(GetString(root, "errorMessage"), secrets) ?? "Render recusado pelo bridge.");
        }

        var output = GetString(root, "outputAbsolutePath");
        if (string.IsNullOrWhiteSpace(output) || !File.Exists(output))
        {
            return ExportFail(EditorResultStatus.Error, "RENDER_NO_OUTPUT", "Arquivo de prévia não encontrado no destino.");
        }

        var outputFull = Path.GetFullPath(output);
        var dirFull = Path.GetFullPath(outputDirectoryAbsolute).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!outputFull.StartsWith(dirFull, StringComparison.OrdinalIgnoreCase))
        {
            return ExportFail(EditorResultStatus.Error, "RENDER_OUTPUT_OUTSIDE", "Prévia gerada fora do diretório de saída configurado.");
        }

        return new EditorExportResultDto(EditorResultStatus.ReadyReal, outputFull, null, null);
    }

    // ---- Auxiliares ----

    /// <summary>Retorna erro de indisponibilidade quando a operação não pode ser chamada agora; null se pode.</summary>
    private static (string Status, string Code, string Message)? RequireOperation(Detection detection, string operation)
    {
        var caps = detection.Capabilities;
        var ready = caps.EnvironmentState is EditorEnvironmentStates.Ready or EditorEnvironmentStates.ReadyPartial;
        if (!ready || detection.Python is null)
        {
            var status = caps.EnvironmentState is EditorEnvironmentStates.Error
                ? EditorResultStatus.Error
                : EditorResultStatus.Unavailable;
            // Código estável para o use case; o estado específico (NOT_RUNNING, SCRIPTING_DISABLED...) vai na mensagem.
            var code = caps.EnvironmentState is EditorEnvironmentStates.Error
                ? caps.ReasonCode ?? EditorUnavailableCode
                : EditorUnavailableCode;
            return (status, code, "[" + caps.EnvironmentState + "] " + caps.StatusMessage);
        }

        var entry = caps.Operations.FirstOrDefault(o => o.Operation == operation);
        if (entry is null || !entry.Supported)
        {
            return (EditorResultStatus.Unavailable, "CAPABILITY_UNSUPPORTED",
                "Operação indisponível neste ambiente: " + (entry?.Note ?? operation));
        }
        return null;
    }

    private static EditorAdapterApplyResult Fail(string status, string code, string message) => new(
        status,
        null,
        Array.Empty<EditorStepResultDto>(),
        Array.Empty<EditorClipMappingDto>(),
        Array.Empty<EditorRejectedOpDto>(),
        Array.Empty<EditorWarningDto>(),
        null,
        code,
        message);

    private static EditorExportResultDto ExportFail(string status, string code, string message) =>
        new(status, null, code, message);

    private static EditorTimelineQueryDto TimelineFail(string projectName, string timelineName, string status, string code, string message) =>
        new(status, projectName, timelineName, null, new List<EditorTimelineItemDto>(), code, message);

    private static List<EditorStepResultDto> ReadSteps(JsonObject root, string?[] secrets)
    {
        var list = new List<EditorStepResultDto>();
        if (root["steps"] is not JsonArray array)
        {
            return list;
        }
        foreach (var node in array)
        {
            if (node is not JsonObject step)
            {
                continue;
            }
            list.Add(new EditorStepResultDto(
                GetString(step, "operation") ?? "unknown",
                GetString(step, "status") ?? "failed",
                Sanitize(GetString(step, "message"), secrets) ?? string.Empty,
                GetLong(step, "durationMs")));
        }
        return list;
    }

    private static List<EditorClipMappingDto> ReadMappings(JsonObject root, EditorAssemblyDto assembly)
    {
        var list = new List<EditorClipMappingDto>();
        if (root["clipMappings"] is not JsonArray array)
        {
            return list;
        }
        foreach (var node in array)
        {
            if (node is not JsonObject mapping || !Guid.TryParse(GetString(mapping, "clipId"), out var clipId))
            {
                continue;
            }

            var observedRecord = GetLong(mapping, "observedRecordFrame");
            var observedDuration = GetLong(mapping, "observedDurationFrames");
            var observedLeftOffset = GetLong(mapping, "leftOffsetFrames");
            var matches = GetBoolOrNull(mapping, "matches");

            // A bridge só diz "casou" por posição/duração. O adaptador exige também o in-point observado.
            // Regra: true só com posição, duração e in-point conferidos; null = posição/duração batem mas in-point
            // não observado (não é conferido); false = divergência ou clipe desconhecido.
            if (matches == true)
            {
                var clip = assembly.Clips.FirstOrDefault(c => c.ClipId == clipId);
                matches = clip is null
                    ? false
                    : ConfirmsManifest(clip, observedRecord, observedDuration, observedLeftOffset);
            }

            list.Add(new EditorClipMappingDto(clipId, GetString(mapping, "editorItemId"),
                observedRecord, observedDuration, matches));
        }
        return list;
    }

    /// <summary>
    /// Posição e duração devem bater (false se não). Com in-point observado, ele deve bater (true/false).
    /// Sem in-point observado devolve null: o casamento não é completo, então não é "conferido".
    /// </summary>
    private static bool? ConfirmsManifest(EditorAssemblyClipDto clip, long? observedRecord, long? observedDuration, long? observedLeftOffset)
    {
        if (observedRecord != clip.RecordFrame || observedDuration != clip.DurationFrames)
        {
            return false;
        }
        if (observedLeftOffset is null)
        {
            return null;
        }
        return observedLeftOffset == clip.SourceStartFrame;
    }

    private static List<EditorRejectedOpDto> ReadRejected(JsonObject root, string?[] secrets)
    {
        var list = new List<EditorRejectedOpDto>();
        if (root["rejected"] is not JsonArray array)
        {
            return list;
        }
        foreach (var node in array)
        {
            if (node is not JsonObject item)
            {
                continue;
            }
            Guid? clipId = Guid.TryParse(GetString(item, "clipId"), out var parsed) ? parsed : null;
            list.Add(new EditorRejectedOpDto(
                GetString(item, "kind") ?? "clip",
                clipId,
                GetString(item, "code") ?? "UNKNOWN",
                Sanitize(GetString(item, "message"), secrets) ?? string.Empty));
        }
        return list;
    }

    private static List<EditorWarningDto> ReadWarnings(JsonObject root, string?[] secrets)
    {
        var list = new List<EditorWarningDto>();
        if (root["warnings"] is not JsonArray array)
        {
            return list;
        }
        foreach (var node in array)
        {
            if (node is not JsonObject item)
            {
                continue;
            }
            Guid? clipId = Guid.TryParse(GetString(item, "clipId"), out var parsed) ? parsed : null;
            list.Add(new EditorWarningDto(
                GetString(item, "code") ?? "WARNING",
                Sanitize(GetString(item, "message"), secrets) ?? string.Empty,
                clipId));
        }
        return list;
    }

    /// <summary>Caminhos que nunca podem aparecer em mensagens (script, Resolve e temporários da chamada).</summary>
    private string?[] SecretPaths(params string?[] extra)
    {
        var list = new List<string?> { _options.BridgeScriptPath, _options.ResolveExecutablePath };
        list.AddRange(extra);
        return list.ToArray();
    }

    /// <summary>Remove caminhos conhecidos e limita tamanho. Mensagens do bridge já são seguras; isto é defesa extra.</summary>
    private static string? Sanitize(string? text, string?[] secrets)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        var result = text.Trim();
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrEmpty(secret))
            {
                result = result.Replace(secret, "<caminho>", StringComparison.OrdinalIgnoreCase);
            }
        }
        return result.Length > 500 ? result[..500] : result;
    }

    private static string? GetString(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static bool GetBool(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out var node) && node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static bool? GetBoolOrNull(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out var node) && node is JsonValue value && value.TryGetValue<bool>(out var flag)
            ? flag
            : null;

    private static long? GetLong(JsonObject obj, string name) =>
        obj.TryGetPropertyValue(name, out var node) && node is JsonValue value && value.TryGetValue<long>(out var number)
            ? number
            : null;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Temporário: falha ao apagar não altera o resultado da operação.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsResolveProcessRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("Resolve");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            return false;
        }
    }
}
