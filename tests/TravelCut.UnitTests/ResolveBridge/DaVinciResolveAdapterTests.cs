using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.ResolveBridge;
using Xunit;

namespace TravelCut.Tests.ResolveBridge;

// Testes do adaptador real com IProcessRunner falso (sem Python, sem DaVinci). Cobrem detecção,
// protocolo, pass-through de steps/mapeamentos, limpeza de temporários e ausência de caminhos em mensagens.
public sealed class DaVinciResolveAdapterTests : IDisposable
{
    private const string PythonOk = "TRAVELCUT_PY=3.12.4 bits=64";
    private const string ProjectName = "TravelCut_Teste_r0001_20261102T120000";

    private static readonly Guid MediaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClipId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "TravelCut.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _script;
    private readonly string _resolveExe;
    private readonly FakeRunner _runner = new();
    private readonly Dictionary<string, Func<ProcessRunRequest, ProcessRunResult>> _replies = new(StringComparer.Ordinal);
    private Func<ProcessRunResult> _pythonCheck = () => Completed(PythonOk);
    private bool _resolveRunning = true;

    public DaVinciResolveAdapterTests()
    {
        Directory.CreateDirectory(_root);
        _script = Path.Combine(_root, "resolve_bridge.py");
        File.WriteAllText(_script, "# script de teste (nao executado)");
        _resolveExe = Path.Combine(_root, "Resolve.exe");
        File.WriteAllText(_resolveExe, "exe de teste");

        _runner.Handler = request =>
        {
            if (request.Arguments.Contains("-c"))
            {
                return _pythonCheck();
            }
            var command = CommandOf(request);
            return _replies.TryGetValue(command, out var reply)
                ? reply(request)
                : Completed(ProbeJson());
        };
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ---- Detecção ----

    [Fact]
    public async Task NotInstalled_WhenResolveExecutableIsMissing()
    {
        var adapter = Create(Options(Path.Combine(_root, "inexistente", "Resolve.exe")));

        var caps = await adapter.DetectCapabilitiesAsync();

        Assert.Equal(EditorEnvironmentStates.NotInstalled, caps.EnvironmentState);
        Assert.False(caps.IsAvailable);
        Assert.False(caps.IsSimulated);
        Assert.Equal("davinci", caps.AdapterMode);
        Assert.Empty(_runner.Calls);
        Assert.DoesNotContain(_root, caps.StatusMessage);
    }

    [Fact]
    public async Task NotRunning_WhenResolveProcessIsAbsent_AndVersionIsNull()
    {
        _resolveRunning = false;
        var adapter = Create();

        var caps = await adapter.DetectCapabilitiesAsync();

        Assert.Equal(EditorEnvironmentStates.NotRunning, caps.EnvironmentState);
        Assert.False(caps.IsAvailable);
        Assert.Null(caps.Version);
        Assert.Null(caps.ProductName);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task PythonUnavailable_WhenPythonCannotStart()
    {
        _pythonCheck = () => new ProcessRunResult(ProcessRunStatus.NotStarted, null, "", "", "x");
        var adapter = Create(Options(pythonExecutable: null));

        var caps = await adapter.DetectCapabilitiesAsync();

        Assert.Equal(EditorEnvironmentStates.PythonUnavailable, caps.EnvironmentState);
        Assert.False(caps.IsAvailable);
        Assert.DoesNotContain(_runner.Calls, c => c.StandardInput is not null);
    }

    [Fact]
    public async Task PythonUnavailable_WhenPython32Bits()
    {
        _pythonCheck = () => Completed("TRAVELCUT_PY=3.12.4 bits=32");

        var caps = await Create().DetectCapabilitiesAsync();

        Assert.Equal(EditorEnvironmentStates.PythonUnavailable, caps.EnvironmentState);
        Assert.Contains("32 bits", caps.StatusMessage);
    }

    [Fact]
    public async Task ScriptingDisabled_WhenProbeReportsIt()
    {
        _replies["probe"] = _ => Completed(ProbeJson(ok: false, errorCode: "SCRIPTING_DISABLED"));

        var caps = await Create().DetectCapabilitiesAsync();

        Assert.Equal(EditorEnvironmentStates.ScriptingDisabled, caps.EnvironmentState);
        Assert.False(caps.IsAvailable);
        Assert.Null(caps.Version);
        Assert.Contains("Scripting externo", caps.StatusMessage);
    }

    [Fact]
    public async Task Ready_UsesVersionReadFromProbe()
    {
        var caps = await Create().DetectCapabilitiesAsync();

        Assert.Equal(EditorEnvironmentStates.Ready, caps.EnvironmentState);
        Assert.True(caps.IsAvailable);
        Assert.Equal("DaVinci Resolve Studio", caps.ProductName);
        Assert.Equal("20.2.0.0", caps.Version);
        Assert.True(caps.CanApplyEditPlan);
        Assert.True(caps.CanExportPreview);

        var apply = caps.Operations.Single(o => o.Operation == EditorOperations.ApplyEditPlan);
        Assert.True(apply.Supported);
        Assert.Equal(CapabilityVerification.HostPending, apply.Verification);

        var probe = _runner.Calls.Single(c => c.StandardInput is not null);
        Assert.Equal(_script, probe.Arguments.Last());
        Assert.Contains("\"schemaVersion\":\"resolve-bridge/1\"", probe.StandardInput);
        Assert.Contains("\"command\":\"probe\"", probe.StandardInput);
    }

    [Fact]
    public async Task ReadyPartial_WhenAppendToTimelineIsMissing()
    {
        _replies["probe"] = _ => Completed(ProbeJson(methods: new Dictionary<string, bool?> { ["AppendToTimeline"] = false }));

        var caps = await Create().DetectCapabilitiesAsync();

        Assert.Equal(EditorEnvironmentStates.ReadyPartial, caps.EnvironmentState);
        Assert.True(caps.IsAvailable);
        Assert.Equal("METHODS_MISSING", caps.ReasonCode);
        Assert.False(caps.CanApplyEditPlan);
        Assert.True(caps.CanCreateProject);

        var apply = caps.Operations.Single(o => o.Operation == EditorOperations.ApplyEditPlan);
        Assert.False(apply.Supported);
        Assert.Equal(CapabilityVerification.Unsupported, apply.Verification);
    }

    [Fact]
    public async Task NullMethod_IsNotVerifiable_SoOperationIsUnsupported_AndEnvironmentIsReadyPartial()
    {
        _replies["probe"] = _ => Completed(ProbeJson(methods: new Dictionary<string, bool?> { ["GetItemListInTrack"] = null }));

        var caps = await Create().DetectCapabilitiesAsync();

        Assert.Equal(EditorEnvironmentStates.ReadyPartial, caps.EnvironmentState);
        Assert.Equal("METHODS_UNVERIFIED", caps.ReasonCode);
        var query = caps.Operations.Single(o => o.Operation == EditorOperations.QueryTimeline);
        Assert.False(query.Supported);
        Assert.Equal(CapabilityVerification.HostPending, query.Verification);
        Assert.Contains("não verificáveis sem projeto aberto", query.Note);
        var apply = caps.Operations.Single(o => o.Operation == EditorOperations.ApplyEditPlan);
        Assert.False(apply.Supported);
        Assert.False(caps.CanApplyEditPlan);
    }

    [Fact]
    public async Task DetectionCache_UsesInjectedClock()
    {
        var clock = new ManualTimeProvider();
        var adapter = new DaVinciResolveAdapter(_runner, Options(), NullLogger<DaVinciResolveAdapter>.Instance,
            () => _resolveRunning, clock);

        var first = await adapter.DetectCapabilitiesAsync();
        await adapter.DetectCapabilitiesAsync();
        Assert.Equal(1, CountProbes());
        Assert.Equal(clock.Now.UtcDateTime, first.CheckedAtUtc);

        clock.Now = clock.Now.AddSeconds(6);
        await adapter.DetectCapabilitiesAsync();
        Assert.Equal(2, CountProbes());
    }

    [Fact]
    public async Task Error_OnProbeTimeout()
    {
        _replies["probe"] = _ => new ProcessRunResult(ProcessRunStatus.TimedOut, null, "", "");

        var caps = await Create().DetectCapabilitiesAsync();

        Assert.Equal(EditorEnvironmentStates.Error, caps.EnvironmentState);
        Assert.Equal("TIMEOUT", caps.ReasonCode);
        Assert.False(caps.IsAvailable);
    }

    [Fact]
    public async Task Error_OnInvalidProbeStdout()
    {
        _replies["probe"] = _ => Completed("isto nao e json");

        var caps = await Create().DetectCapabilitiesAsync();

        Assert.Equal(EditorEnvironmentStates.Error, caps.EnvironmentState);
        Assert.Equal("BRIDGE_PROTOCOL_ERROR", caps.ReasonCode);
    }

    [Fact]
    public async Task Error_BridgeFailed_WhenNonZeroExitWithoutJson()
    {
        _replies["probe"] = _ => new ProcessRunResult(ProcessRunStatus.Completed, 1, "", "Traceback ...");

        var caps = await Create().DetectCapabilitiesAsync();

        Assert.Equal(EditorEnvironmentStates.Error, caps.EnvironmentState);
        Assert.Equal("BRIDGE_FAILED", caps.ReasonCode);
    }

    [Fact]
    public async Task Error_WhenBridgeScriptIsMissing()
    {
        var options = new ResolveBridgeOptions("fake-python", Path.Combine(_root, "nao-existe.py"),
            _resolveExe, TimeSpan.FromSeconds(5), true);

        var caps = await Create(options).DetectCapabilitiesAsync();

        Assert.Equal(EditorEnvironmentStates.Error, caps.EnvironmentState);
        Assert.Equal("BRIDGE_SCRIPT_MISSING", caps.ReasonCode);
        Assert.DoesNotContain(_root, caps.StatusMessage);
    }

    [Fact]
    public async Task Capabilities_AreUnsupportedOrPending_NeverSimulated()
    {
        var caps = await Create().DetectCapabilitiesAsync();

        Assert.All(caps.Operations, o => Assert.Contains(o.Verification,
            new[] { CapabilityVerification.HostPending, CapabilityVerification.Unsupported }));
        Assert.DoesNotContain(caps.Operations, o => o.Verification == CapabilityVerification.MockPass);
        Assert.DoesNotContain(caps.Operations, o => o.Verification == CapabilityVerification.HostPass);
    }

    // ---- Apply ----

    [Fact]
    public async Task Apply_PassesThroughReadyReal_WithStepsAndMappings()
    {
        // In-point observado (30 = sourceStartFrame do manifesto de teste): só então o casamento é verdadeiro.
        Reply("apply_assembly", _ => Completed(ApplyReplyJson("READY_REAL", matches: true, leftOffsetFrames: 30)));

        var result = await Create().ApplyAssemblyAsync(Assembly(), Options30());

        Assert.Equal(EditorResultStatus.ReadyReal, result.Status);
        Assert.Equal("20.2", result.EditorVersion);
        Assert.Equal(2, result.Steps.Count);
        Assert.Equal("create_project", result.Steps[0].Operation);
        Assert.Equal("ok", result.Steps[0].Status);
        Assert.Single(result.ClipMappings);
        Assert.Equal(ClipId, result.ClipMappings[0].ClipId);
        Assert.True(result.ClipMappings[0].Matches);
        Assert.Equal("item-1", result.ClipMappings[0].EditorItemId);
        Assert.Null(result.ErrorCode);
        Assert.Null(result.SnapshotAbsolutePath);
    }

    [Fact]
    public async Task Apply_InPointNotObserved_MatchIsNull_NeverConfirmed()
    {
        // A3: posição e duração batem, mas o bridge não expôs leftOffset: matches null (não true).
        Reply("apply_assembly", _ => Completed(ApplyReplyJson("PARTIAL_REAL", matches: true)));

        var result = await Create().ApplyAssemblyAsync(Assembly(), Options30());

        Assert.Null(result.ClipMappings[0].Matches);
        Assert.Equal(EditorResultStatus.PartialReal, result.Status);
        Assert.Equal(120, result.ClipMappings[0].ObservedDurationFrames);
    }

    [Fact]
    public async Task Apply_PassesThroughPartialReal_WithRejectionAndDivergence()
    {
        Reply("apply_assembly", _ => Completed(ApplyReplyJson("PARTIAL_REAL", matches: false,
            rejectedCode: "MEDIA_NOT_FOUND")));

        var result = await Create().ApplyAssemblyAsync(Assembly(), Options30());

        Assert.Equal(EditorResultStatus.PartialReal, result.Status);
        Assert.Single(result.Rejected);
        Assert.Equal("MEDIA_NOT_FOUND", result.Rejected[0].Code);
        Assert.False(result.ClipMappings[0].Matches);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task Apply_SendsSchemaAndManifestPath_ManifestExistsDuringCall_AndIsRemovedAfter()
    {
        string? manifestPath = null;
        string? manifestDuringCall = null;
        Reply("apply_assembly", request =>
        {
            var body = JsonNode.Parse(request.StandardInput!.Trim())!.AsObject();
            manifestPath = body["manifestPath"]!.GetValue<string>();
            manifestDuringCall = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : null;
            Assert.Equal("resolve-bridge/1", body["schemaVersion"]!.GetValue<string>());
            Assert.Equal("apply_assembly", body["command"]!.GetValue<string>());
            Assert.True(body["endFrameInclusive"]!.GetValue<bool>());
            return Completed(ApplyReplyJson("READY_REAL", matches: true));
        });

        await Create().ApplyAssemblyAsync(Assembly(), Options30());

        Assert.NotNull(manifestPath);
        Assert.Contains("\"schemaVersion\":\"editor-assembly/1\"", manifestDuringCall);
        Assert.Contains(ProjectName, manifestDuringCall);
        Assert.False(File.Exists(manifestPath));
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "TravelCut", "bridge"), manifestPath);
    }

    [Fact]
    public async Task Apply_TimeoutReturnsError_AndRemovesTemporaryManifest()
    {
        string? manifestPath = null;
        Reply("apply_assembly", request =>
        {
            manifestPath = JsonNode.Parse(request.StandardInput!.Trim())!["manifestPath"]!.GetValue<string>();
            return new ProcessRunResult(ProcessRunStatus.TimedOut, null, "", "");
        });

        var result = await Create().ApplyAssemblyAsync(Assembly(), Options30());

        Assert.Equal(EditorResultStatus.Error, result.Status);
        Assert.Equal("TIMEOUT", result.ErrorCode);
        Assert.NotNull(manifestPath);
        Assert.False(File.Exists(manifestPath));
    }

    [Fact]
    public async Task Apply_InvalidStdout_IsProtocolErrorNeverSuccess()
    {
        Reply("apply_assembly", _ => Completed(""));

        var result = await Create().ApplyAssemblyAsync(Assembly(), Options30());

        Assert.Equal(EditorResultStatus.Error, result.Status);
        Assert.Equal("BRIDGE_PROTOCOL_ERROR", result.ErrorCode);
        Assert.Empty(result.ClipMappings);
    }

    [Fact]
    public async Task Apply_BridgeErrorIsReportedWithoutAbsolutePaths()
    {
        string? manifestPath = null;
        Reply("apply_assembly", request =>
        {
            manifestPath = JsonNode.Parse(request.StandardInput!.Trim())!["manifestPath"]!.GetValue<string>();
            // Simula bridge vazando caminho na mensagem: o adaptador deve substituí-lo.
            return Completed(ApplyReplyJson("ERROR", matches: false, ok: false, errorCode: "PROJECT_EXISTS",
                errorMessage: "Falhou ao usar " + manifestPath));
        });

        var result = await Create().ApplyAssemblyAsync(Assembly(), Options30());

        Assert.Equal(EditorResultStatus.Error, result.Status);
        Assert.Equal("PROJECT_EXISTS", result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.DoesNotContain(manifestPath!, result.ErrorMessage);
        Assert.Contains("<caminho>", result.ErrorMessage);
    }

    [Fact]
    public async Task Apply_RefusesPersonalProjectName_WithoutCallingBridge()
    {
        var result = await Create().ApplyAssemblyAsync(Assembly("Meu Projeto Pessoal"), Options30());

        Assert.Equal(EditorResultStatus.Error, result.Status);
        Assert.Equal("PERMISSION_DENIED", result.ErrorCode);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Apply_ReturnsUnavailable_WhenApplyCapabilityIsUnsupported()
    {
        _replies["probe"] = _ => Completed(ProbeJson(methods: new Dictionary<string, bool?> { ["AppendToTimeline"] = false }));

        var result = await Create().ApplyAssemblyAsync(Assembly(), Options30());

        Assert.Equal(EditorResultStatus.Unavailable, result.Status);
        Assert.Equal("CAPABILITY_UNSUPPORTED", result.ErrorCode);
        Assert.DoesNotContain(_runner.Calls, c => c.StandardInput is not null && c.StandardInput.Contains("apply_assembly"));
    }

    [Fact]
    public async Task Apply_ReturnsUnavailable_WhenResolveNotRunning()
    {
        _resolveRunning = false;

        var result = await Create().ApplyAssemblyAsync(Assembly(), Options30());

        Assert.Equal(EditorResultStatus.Unavailable, result.Status);
        Assert.Equal("EDITOR_UNAVAILABLE", result.ErrorCode);
        Assert.Contains(EditorEnvironmentStates.NotRunning, result.ErrorMessage);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Apply_WrongManifestSchema_IsContractVersionMismatch()
    {
        var result = await Create().ApplyAssemblyAsync(Assembly() with { SchemaVersion = "editor-assembly/9" }, Options30());

        Assert.Equal(EditorResultStatus.Error, result.Status);
        Assert.Equal("CONTRACT_VERSION_MISMATCH", result.ErrorCode);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Apply_SnapshotReportedOnlyWhenStepOkAndFileExists()
    {
        var snapshot = ArtifactPath("snapshot.drp");
        Reply("apply_assembly", _ =>
        {
            File.WriteAllText(snapshot, "conteudo do teste (nao e um .drp real)");
            return Completed(ApplyReplyJson("READY_REAL", matches: true, snapshotStepOk: true));
        });

        var result = await Create().ApplyAssemblyAsync(Assembly(),
            new EditorApplyOptions(snapshot, TimeSpan.FromSeconds(30)));

        Assert.Equal(snapshot, result.SnapshotAbsolutePath);
    }

    [Fact]
    public async Task Apply_SnapshotNullWhenFileNotProduced()
    {
        var snapshot = ArtifactPath("ausente.drp");
        Reply("apply_assembly", _ => Completed(ApplyReplyJson("READY_REAL", matches: true, snapshotStepOk: true)));

        var result = await Create().ApplyAssemblyAsync(Assembly(),
            new EditorApplyOptions(snapshot, TimeSpan.FromSeconds(30)));

        Assert.Null(result.SnapshotAbsolutePath);
    }

    // ---- Timeline / exportação ----

    [Fact]
    public async Task QueryTimeline_MapsItemsRelativeToBridgeResponse()
    {
        Reply("query_timeline", _ => Completed(new JsonObject
        {
            ["schemaVersion"] = "resolve-bridge/1",
            ["ok"] = true,
            ["status"] = "READY_REAL",
            ["timelineStartFrame"] = 86400,
            ["items"] = new JsonArray(new JsonObject
            {
                ["editorItemId"] = "item-9",
                ["trackIndex"] = 1,
                ["recordFrame"] = 120,
                ["durationFrames"] = 90,
                ["leftOffsetFrames"] = 30,
                ["mediaName"] = "clip.mp4",
            }),
        }.ToJsonString()));

        var query = await Create().QueryTimelineAsync(ProjectName, "TravelCut Timeline");

        Assert.Equal(EditorResultStatus.ReadyReal, query.Status);
        Assert.Equal(86400, query.TimelineStartFrame);
        var item = Assert.Single(query.Items);
        Assert.Equal(120, item.RecordFrame);
        Assert.Equal(90, item.DurationFrames);
        Assert.Equal(30, item.SourceStartFrame);
    }

    [Fact]
    public async Task QueryTimeline_SourceStartIsNull_WhenBridgeDoesNotReportLeftOffset()
    {
        Reply("query_timeline", _ => Completed(new JsonObject
        {
            ["schemaVersion"] = "resolve-bridge/1",
            ["ok"] = true,
            ["status"] = "READY_REAL",
            ["items"] = new JsonArray(new JsonObject { ["trackIndex"] = 1, ["recordFrame"] = 0, ["durationFrames"] = 120 }),
        }.ToJsonString()));

        var query = await Create().QueryTimelineAsync(ProjectName, "TravelCut Timeline");

        Assert.Null(Assert.Single(query.Items).SourceStartFrame);
    }

    [Fact]
    public async Task QueryTimeline_RefusesNonTravelCutProject()
    {
        var query = await Create().QueryTimelineAsync("Pessoal_2026", "x");

        Assert.Equal("PERMISSION_DENIED", query.ErrorCode);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task ExportProject_RefusesNonTravelCutProject()
    {
        var result = await Create().ExportProjectAsync("Pessoal_2026", Path.Combine(_root, "x.drp"));

        Assert.Equal("PERMISSION_DENIED", result.ErrorCode);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task ExportPreview_ReturnsReadyOnlyWhenFileExistsInsideOutputDir()
    {
        var outDir = Path.Combine(_root, "3-Trabalho", "editor", "plano", "previews", "x");
        Directory.CreateDirectory(outDir);
        var output = Path.Combine(outDir, "TravelCut_preview.mp4");
        Reply("render_preview", _ =>
        {
            File.WriteAllText(output, "mp4 de teste (nao renderizado)");
            return Completed(new JsonObject
            {
                ["schemaVersion"] = "resolve-bridge/1",
                ["ok"] = true,
                ["status"] = "READY_REAL",
                ["outputAbsolutePath"] = output,
            }.ToJsonString());
        });

        var result = await Create().ExportPreviewAsync(ProjectName, "TravelCut Timeline", outDir);

        Assert.Equal(EditorResultStatus.ReadyReal, result.Status);
        Assert.Equal(Path.GetFullPath(output), result.OutputAbsolutePath);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task ExportProject_RefusesOutputOutsideEditorFolder_WithoutCallingBridge()
    {
        var result = await Create().ExportProjectAsync(ProjectName, Path.Combine(_root, "fora.drp"));

        Assert.Equal("PERMISSION_DENIED", result.ErrorCode);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task ExportPreview_RefusesOutputDirOutsideEditorFolder_WithoutCallingBridge()
    {
        var result = await Create().ExportPreviewAsync(ProjectName, "TravelCut Timeline", Path.Combine(_root, "render"));

        Assert.Equal("PERMISSION_DENIED", result.ErrorCode);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task ExportProject_SendsAllowedOutputRootDerivedFromPath()
    {
        var output = ArtifactPath("export.drp");
        string? sentRoot = null;
        Reply("export_project", request =>
        {
            sentRoot = JsonNode.Parse(request.StandardInput!.Trim())!["allowedOutputRoot"]!.GetValue<string>();
            File.WriteAllText(output, "drp de teste (nao e um .drp real)");
            return Completed(new JsonObject { ["schemaVersion"] = "resolve-bridge/1", ["ok"] = true, ["status"] = "READY_REAL" }.ToJsonString());
        });

        var result = await Create().ExportProjectAsync(ProjectName, output);

        Assert.Equal(EditorResultStatus.ReadyReal, result.Status);
        Assert.Equal(Path.Combine(_root, "3-Trabalho", "editor"), sentRoot);
    }

    [Fact]
    public async Task Apply_RefusesSnapshotOutsideEditorFolder_WithoutCallingBridge()
    {
        var snapshot = Path.Combine(_root, "fora", "snapshot.drp");

        var result = await Create().ApplyAssemblyAsync(Assembly(), new EditorApplyOptions(snapshot, TimeSpan.FromSeconds(30)));

        Assert.Equal("PERMISSION_DENIED", result.ErrorCode);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Apply_RefusesTraversalThatLeavesEditorFolder()
    {
        // Path.GetFullPath resolve ".." antes da checagem: o caminho final sai de 3-Trabalho\editor.
        var snapshot = Path.Combine(_root, "3-Trabalho", "editor", "..", "..", "pessoal", "x.drp");

        var result = await Create().ApplyAssemblyAsync(Assembly(), new EditorApplyOptions(snapshot, TimeSpan.FromSeconds(30)));

        Assert.Equal("PERMISSION_DENIED", result.ErrorCode);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Apply_RefusesSnapshotFromAnotherProjectThanManifest()
    {
        var manifest = ArtifactFile("plano-a", "r0001.assembly.json");
        var snapshot = Path.Combine(_root, "projeto-b", "3-Trabalho", "editor", "plano-b", "snapshots", "x.drp");

        var result = await Create().ApplyAssemblyAsync(Assembly(), new EditorApplyOptions(snapshot, TimeSpan.FromSeconds(30), manifest));

        Assert.Equal("PERMISSION_DENIED", result.ErrorCode);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Apply_RefusesManifestOutsideEditorFolder()
    {
        var manifest = Path.Combine(_root, "r0001.assembly.json");
        File.WriteAllText(manifest, "{}");

        var result = await Create().ApplyAssemblyAsync(Assembly(), new EditorApplyOptions(null, TimeSpan.FromSeconds(30), manifest));

        Assert.Equal("PERMISSION_DENIED", result.ErrorCode);
        Assert.Empty(_runner.Calls);
    }

    [Fact]
    public async Task Apply_UsesManifestAlreadyWritten_WithoutTemporaryCopy_AndSendsAllowedRoot()
    {
        var manifest = ArtifactFile("plano", "r0001.assembly.json");
        string? sentManifest = null;
        string? sentRoot = null;
        Reply("apply_assembly", request =>
        {
            var body = JsonNode.Parse(request.StandardInput!.Trim())!.AsObject();
            sentManifest = body["manifestPath"]!.GetValue<string>();
            sentRoot = body["allowedOutputRoot"]!.GetValue<string>();
            return Completed(ApplyReplyJson("READY_REAL", matches: true));
        });

        var result = await Create().ApplyAssemblyAsync(Assembly(), new EditorApplyOptions(null, TimeSpan.FromSeconds(30), manifest));

        Assert.Equal(EditorResultStatus.ReadyReal, result.Status);
        Assert.Equal(manifest, sentManifest);
        Assert.Equal(Path.Combine(_root, "3-Trabalho", "editor"), sentRoot);
        Assert.True(File.Exists(manifest), "o manifesto pertence ao use case e não pode ser removido pelo adaptador");
        Assert.DoesNotContain(Path.Combine(Path.GetTempPath(), "TravelCut", "bridge"), sentManifest);
    }

    [Theory]
    [InlineData(30L, true)]    // in-point observado igual ao sourceStartFrame do manifesto
    [InlineData(31L, false)]   // in-point divergente: matches cai mesmo com posição e duração corretas
    public async Task Apply_MatchRequiresInPointWhenObserved(long leftOffset, bool expected)
    {
        Reply("apply_assembly", _ => Completed(ApplyReplyJson("READY_REAL", matches: true, leftOffsetFrames: leftOffset)));

        var result = await Create().ApplyAssemblyAsync(Assembly(), Options30());

        Assert.Equal(expected, result.ClipMappings[0].Matches);
    }

    [Theory]
    [InlineData(5L, 120L)]     // posição divergente
    [InlineData(0L, 119L)]     // duração divergente
    public async Task Apply_MatchRequiresRecordAndDuration_EvenWhenBridgeSaysTrue(long record, long duration)
    {
        Reply("apply_assembly", _ => Completed(ApplyReplyJson("PARTIAL_REAL", matches: true,
            observedRecord: record, observedDuration: duration)));

        var result = await Create().ApplyAssemblyAsync(Assembly(), Options30());

        Assert.False(result.ClipMappings[0].Matches);
    }

    [Fact]
    public async Task ResolveCalls_NeverOverlap_WhenRequestedConcurrently()
    {
        var runner = new OverlapRunner();
        var adapter = new DaVinciResolveAdapter(runner, Options(), NullLogger<DaVinciResolveAdapter>.Instance, () => true);

        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => adapter.QueryTimelineAsync(ProjectName, "TravelCut Timeline")));

        Assert.All(results, r => Assert.Equal(EditorResultStatus.ReadyReal, r.Status));
        Assert.Equal(4, runner.ResolveCalls);
        Assert.Equal(1, runner.MaxConcurrentResolveCalls);
    }

    [Fact]
    public async Task ResolveCall_WaitingForGate_IsCancellable()
    {
        var runner = new OverlapRunner { Hold = true };
        var adapter = new DaVinciResolveAdapter(runner, Options(), NullLogger<DaVinciResolveAdapter>.Instance, () => true);
        var first = adapter.QueryTimelineAsync(ProjectName, "TravelCut Timeline");
        await runner.Started.Task;

        using var cts = new CancellationTokenSource();
        var second = adapter.QueryTimelineAsync(ProjectName, "TravelCut Timeline", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        runner.Release();
        Assert.Equal(EditorResultStatus.ReadyReal, (await first).Status);
        Assert.Equal(1, runner.ResolveCalls);
    }

    [Fact]
    public async Task DetectCapabilities_WhileApplyIsInFlight_DoesNotCallRunner_AndReturnsMarkedLastState()
    {
        var runner = new GatedApplyRunner();
        var adapter = new DaVinciResolveAdapter(runner, Options(), NullLogger<DaVinciResolveAdapter>.Instance, () => true);
        var apply = adapter.ApplyAssemblyAsync(Assembly(), Options30());
        await runner.ApplyStarted.Task;
        var callsBefore = runner.Calls;

        var caps = await adapter.DetectCapabilitiesAsync();

        Assert.Equal(callsBefore, runner.Calls);                 // nenhuma sonda enquanto o Resolve está ocupado
        Assert.EndsWith(" (operação em andamento no Resolve; estado da última verificação)", caps.StatusMessage);
        Assert.Equal(EditorEnvironmentStates.Ready, caps.EnvironmentState);
        Assert.True(caps.IsAvailable);

        runner.Release();
        Assert.Equal(EditorResultStatus.ReadyReal, (await apply).Status);
    }

    [Fact]
    public async Task DetectCapabilities_AfterApplyFinishes_ProbesAgainWhenCacheExpired()
    {
        var clock = new ManualTimeProvider();
        var runner = new GatedApplyRunner();
        var adapter = new DaVinciResolveAdapter(runner, Options(), NullLogger<DaVinciResolveAdapter>.Instance, () => true, clock);
        var apply = adapter.ApplyAssemblyAsync(Assembly(), Options30());
        await runner.ApplyStarted.Task;
        runner.Release();
        await apply;

        clock.Now = clock.Now.AddMinutes(1);
        var before = runner.Calls;
        var caps = await adapter.DetectCapabilitiesAsync();

        Assert.True(runner.Calls > before);                      // gate livre: sonda de novo
        Assert.DoesNotContain("operação em andamento", caps.StatusMessage);
    }

    [Theory]
    [InlineData("SaveProject")]
    [InlineData("SetCurrentTimeline")]
    [InlineData("GetMediaPool")]
    public async Task ApplyEditPlan_RequiresSaveTimelineAndMediaPoolMethods(string missing)
    {
        var caps = await ProbeWithMissing(missing);

        var op = OperationOf(caps, EditorOperations.ApplyEditPlan);
        Assert.False(op.Supported);
        Assert.Contains(missing, op.Note);
    }

    [Theory]
    [InlineData("SetRenderSettings")]
    [InlineData("AddRenderJob")]
    [InlineData("StartRendering")]
    [InlineData("IsRenderingInProgress")]
    public async Task ExportPreview_RequiresRenderSettingsAndJobMethods(string missing)
    {
        var caps = await ProbeWithMissing(missing);

        var op = OperationOf(caps, EditorOperations.ExportPreview);
        Assert.False(op.Supported);
        Assert.Contains(missing, op.Note);
    }

    [Fact]
    public async Task ExportPreview_DoesNotRequireSetCurrentRenderFormatAndCodec()
    {
        // Decisão documentada: o formato mp4/H264 é aplicado por hasattr no bridge; ausência não derruba a operação.
        var caps = await ProbeWithMissing("SetCurrentRenderFormatAndCodec");

        Assert.True(OperationOf(caps, EditorOperations.ExportPreview).Supported);
    }

    [Fact]
    public async Task ExportProjectAndSnapshot_RequireExportProjectMethod()
    {
        var caps = await ProbeWithMissing("ExportProject");

        Assert.False(OperationOf(caps, EditorOperations.ExportProject).Supported);
        Assert.False(OperationOf(caps, EditorOperations.CreateSnapshot).Supported);
    }

    [Fact]
    public void ProbedMethods_IncludeSaveAndRenderMethodsRequiredByOperations()
    {
        foreach (var name in new[]
        {
            "SaveProject", "SetCurrentTimeline", "GetMediaPool", "SetRenderSettings", "AddRenderJob",
            "StartRendering", "IsRenderingInProgress", "SetCurrentRenderFormatAndCodec", "ExportProject",
        })
        {
            Assert.Contains(name, DaVinciResolveAdapter.ProbedMethods);
        }
    }

    // ---- Helpers ----

    private async Task<EditorCapabilitiesDto> ProbeWithMissing(string method)
    {
        _replies["probe"] = _ => Completed(ProbeJson(methods: new Dictionary<string, bool?> { [method] = false }));
        return await Create().DetectCapabilitiesAsync();
    }

    private static EditorOperationCapabilityDto OperationOf(EditorCapabilitiesDto caps, string operation) =>
        caps.Operations.Single(o => o.Operation == operation);

    /// <summary>Caminho dentro de &lt;raiz&gt;\3-Trabalho\editor\plano (cria a pasta).</summary>
    private string ArtifactPath(string name)
    {
        var dir = Path.Combine(_root, "3-Trabalho", "editor", "plano");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    private string ArtifactFile(string plan, string name)
    {
        var dir = Path.Combine(_root, "3-Trabalho", "editor", plan);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, name);
        File.WriteAllText(file, "{}");
        return file;
    }

    private int CountProbes() =>
        _runner.Calls.Count(c => c.StandardInput is not null && c.StandardInput.Contains("\"command\":\"probe\""));

    private sealed class ManualTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>Runner que segura o apply_assembly até <see cref="Release"/> (apply lento); conta todas as chamadas.</summary>
    private sealed class GatedApplyRunner : IProcessRunner
    {
        private readonly object _sync = new();
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public TaskCompletionSource ApplyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get { lock (_sync) return _calls; } }

        public void Release() => _release.TrySetResult();

        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct = default)
        {
            lock (_sync)
            {
                _calls++;
            }

            if (request.Arguments.Contains("-c"))
            {
                return Completed(PythonOk);
            }

            if (CommandOf(request) != "apply_assembly")
            {
                return Completed(ProbeJson());
            }

            ApplyStarted.TrySetResult();
            await _release.Task.WaitAsync(ct);
            return Completed(ApplyReplyJson("READY_REAL", matches: true, leftOffsetFrames: 30));
        }
    }

    /// <summary>Runner assíncrono que mede sobreposição de chamadas ao Resolve (apply/query/export/render).</summary>
    private sealed class OverlapRunner : IProcessRunner
    {
        private readonly object _sync = new();
        private int _current;
        private int _max;
        private int _resolveCalls;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Hold { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ResolveCalls { get { lock (_sync) return _resolveCalls; } }
        public int MaxConcurrentResolveCalls { get { lock (_sync) return _max; } }

        public void Release() => _release.TrySetResult();

        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct = default)
        {
            if (request.Arguments.Contains("-c"))
            {
                return Completed(PythonOk);
            }

            var command = CommandOf(request);
            if (command == "probe")
            {
                return Completed(ProbeJson());
            }

            lock (_sync)
            {
                _current++;
                _resolveCalls++;
                _max = Math.Max(_max, _current);
            }
            try
            {
                Started.TrySetResult();
                if (Hold)
                {
                    await _release.Task.WaitAsync(ct);
                }
                else
                {
                    await Task.Delay(40, ct);
                }
                return Completed(new JsonObject
                {
                    ["schemaVersion"] = "resolve-bridge/1",
                    ["ok"] = true,
                    ["status"] = "READY_REAL",
                    ["items"] = new JsonArray(),
                    ["timelineStartFrame"] = 0,
                }.ToJsonString());
            }
            finally
            {
                lock (_sync)
                {
                    _current--;
                }
            }
        }
    }

    private DaVinciResolveAdapter Create(ResolveBridgeOptions? options = null) =>
        new(_runner, options ?? Options(), NullLogger<DaVinciResolveAdapter>.Instance, () => _resolveRunning);

    private ResolveBridgeOptions Options(string? resolvePath = null, string? pythonExecutable = "fake-python") =>
        new(pythonExecutable, _script, resolvePath ?? _resolveExe, TimeSpan.FromSeconds(5), EndFrameInclusive: true);

    private static EditorApplyOptions Options30() => new(null, TimeSpan.FromSeconds(30));

    private void Reply(string command, Func<ProcessRunRequest, ProcessRunResult> reply) => _replies[command] = reply;

    private static ProcessRunResult Completed(string stdout) => new(ProcessRunStatus.Completed, 0, stdout, "");

    private static string CommandOf(ProcessRunRequest request) =>
        JsonNode.Parse(request.StandardInput!.Trim())!["command"]!.GetValue<string>();

    private static string ProbeJson(
        bool ok = true,
        string? errorCode = null,
        IReadOnlyDictionary<string, bool?>? methods = null)
    {
        var methodNode = new JsonObject();
        foreach (var name in DaVinciResolveAdapter.ProbedMethods)
        {
            bool? value = true;
            if (methods is not null && methods.TryGetValue(name, out var explicitValue))
            {
                value = explicitValue;
            }
            methodNode[name] = value is null ? null : JsonValue.Create(value.Value);
        }

        return new JsonObject
        {
            ["schemaVersion"] = "resolve-bridge/1",
            ["ok"] = ok,
            ["status"] = "READY",
            ["errorCode"] = errorCode,
            ["errorMessage"] = errorCode is null ? null : "Scripting externo indisponível (teste).",
            ["productName"] = ok ? "DaVinci Resolve Studio" : null,
            ["version"] = ok ? "20.2.0.0" : null,
            ["methods"] = methodNode,
        }.ToJsonString();
    }

    private static string ApplyReplyJson(
        string status,
        bool matches,
        bool ok = true,
        string? errorCode = null,
        string? errorMessage = null,
        string? rejectedCode = null,
        bool snapshotStepOk = false,
        long? leftOffsetFrames = null,
        long observedRecord = 0,
        long observedDuration = 120)
    {
        var steps = new JsonArray(
            new JsonObject { ["operation"] = "create_project", ["status"] = "ok", ["message"] = "Projeto novo criado.", ["durationMs"] = 3 },
            new JsonObject { ["operation"] = "apply_edit_plan", ["status"] = "ok", ["message"] = "1 de 1 clipes aplicados.", ["durationMs"] = 5 });
        if (snapshotStepOk)
        {
            steps.Add(new JsonObject { ["operation"] = "create_snapshot", ["status"] = "ok", ["message"] = "Snapshot exportado.", ["durationMs"] = 7 });
        }

        var rejected = new JsonArray();
        if (rejectedCode is not null)
        {
            rejected.Add(new JsonObject
            {
                ["kind"] = "clip",
                ["clipId"] = ClipId.ToString(),
                ["code"] = rejectedCode,
                ["message"] = "Mídia ausente (teste).",
            });
        }

        var mapping = new JsonObject
        {
            ["clipId"] = ClipId.ToString(),
            ["editorItemId"] = "item-1",
            ["observedRecordFrame"] = observedRecord,
            ["observedDurationFrames"] = observedDuration,
            ["matches"] = matches,
        };
        if (leftOffsetFrames is { } leftOffset)
        {
            mapping["leftOffsetFrames"] = leftOffset;
        }

        return new JsonObject
        {
            ["schemaVersion"] = "resolve-bridge/1",
            ["ok"] = ok,
            ["status"] = status,
            ["errorCode"] = errorCode,
            ["errorMessage"] = errorMessage,
            ["editorProjectName"] = ProjectName,
            ["timelineName"] = "TravelCut Timeline",
            ["editorVersion"] = "20.2",
            ["steps"] = steps,
            ["clipMappings"] = new JsonArray(mapping),
            ["rejected"] = rejected,
            ["warnings"] = new JsonArray(),
        }.ToJsonString();
    }

    private static EditorAssemblyDto Assembly(string projectName = ProjectName) => new(
        SchemaVersion: "editor-assembly/1",
        Notice: "Manifesto de teste.",
        ProjectId: Guid.NewGuid(),
        PlanId: Guid.NewGuid(),
        Revision: 1,
        PlanFingerprint: "plan-teste",
        EditorProjectName: projectName,
        TimelineName: "TravelCut Timeline",
        TimeBase: new TimelineTimeBaseDto(1001, 30000, "29,97 fps"),
        TimelineFrameRate: "29.97",
        Media: new List<EditorAssemblyMediaDto>
        {
            new(MediaId, "a.mp4", Path.Combine("C:\\", "midia", "a.mp4"), 30000, 1001, false, 10_000_000),
        },
        Clips: new List<EditorAssemblyClipDto>
        {
            new(ClipId, MediaId, 1, "video_audio", 30, 150, 0, 120, 1_001_000, 5_005_000),
        },
        Rejected: new List<EditorRejectedOpDto>(),
        Warnings: new List<EditorWarningDto>(),
        AssemblyFingerprint: "harness");

    private sealed class FakeRunner : IProcessRunner
    {
        public List<ProcessRunRequest> Calls { get; } = new();

        public Func<ProcessRunRequest, ProcessRunResult> Handler { get; set; } =
            _ => new ProcessRunResult(ProcessRunStatus.NotStarted, null, "", "", "nao configurado");

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            return Task.FromResult(Handler(request));
        }
    }
}
