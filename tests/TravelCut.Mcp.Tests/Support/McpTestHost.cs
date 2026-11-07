using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.ValueObjects;
using TravelCut.Infrastructure.Composition;
using TravelCut.Mcp.Configuration;
using TravelCut.Mcp.Facade;
using TravelCut.Mcp.Tools;
using TravelCut.Infrastructure.Persistence;
using Xunit;

namespace TravelCut.Mcp.Tests.Support;

/// <summary>
/// Host de teste: DI real do Application Core (AddTravelCutCore) sobre SQLite temporário em arquivo,
/// com o simulador do editor (MockResolveAdapter) e um job manager de análise dublê que conta chamadas.
/// Ferramentas são as classes reais; o caminho até o IpcDispatcher é o mesmo da UI.
/// </summary>
public sealed class McpTestHost : IDisposable
{
    public static readonly Guid ProjectA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    public static readonly Guid ProjectB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");
    public static readonly Guid MediaA = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000a1");
    public static readonly Guid MediaA2 = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000000a2");
    public static readonly Guid MediaB = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000000b1");
    public static readonly Guid SuggestionA = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000005a1");
    public static readonly Guid SuggestionA2 = Guid.Parse("aaaaaaaa-0000-4000-8000-0000000005a2");
    public static readonly Guid SuggestionB = Guid.Parse("bbbbbbbb-0000-4000-8000-0000000005b1");

    private readonly ServiceProvider _provider;

    private McpTestHost(string root, ServiceProvider provider, TestPipelineJobManager pipeline, McpServerConfiguration configuration)
    {
        Root = root;
        _provider = provider;
        Pipeline = pipeline;
        Configuration = configuration;
        Read = new TravelCutReadTools(provider.GetRequiredService<TravelCutMcpFacade>());
        Mutations = new TravelCutMutationTools(provider.GetRequiredService<TravelCutMcpFacade>());
    }

    /// <summary>Pasta temporária: raiz do projeto A (contém 1-Bruto etc.) e banco SQLite.</summary>
    public string Root { get; }
    public string DatabasePath => Path.Combine(Root, "db", "travelcut.db");
    public TestPipelineJobManager Pipeline { get; }
    public McpServerConfiguration Configuration { get; }
    public TravelCutReadTools Read { get; }
    public TravelCutMutationTools Mutations { get; }
    public IServiceProvider Services => _provider;

    /// <summary>Cria o host com banco vazio; chame <see cref="SeedAsync"/> para dados de referência.</summary>
    public static McpTestHost Create(bool allowMutations = true, params Guid[] authorizedProjects) =>
        Build(allowMutations, decorateEditor: null, authorizedProjects);

    /// <summary>Host com mutações e com o adaptador do editor decorado (ex.: um que falha de propósito no apply).</summary>
    public static McpTestHost CreateWithEditor(Func<IEditorAdapter, IEditorAdapter> decorateEditor, params Guid[] authorizedProjects) =>
        Build(allowMutations: true, decorateEditor, authorizedProjects);

    private static McpTestHost Build(bool allowMutations, Func<IEditorAdapter, IEditorAdapter>? decorateEditor, Guid[] authorizedProjects)
    {
        var root = Path.Combine(Path.GetTempPath(), "tc_mcp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var configuration = new McpServerConfiguration(allowMutations, new HashSet<Guid>(authorizedProjects), null);
        var pipeline = new TestPipelineJobManager();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(configuration);
        services.AddTravelCutCore(new TravelCutHostOptions
        {
            DatabasePath = Path.Combine(root, "db", "travelcut.db"),
            AppBaseDirectory = AppContext.BaseDirectory,
            UseMockEditor = true,
        });
        services.Replace(ServiceDescriptor.Singleton<IPipelineJobManager>(pipeline));
        if (decorateEditor is not null)
        {
            services.Replace(ServiceDescriptor.Singleton<IEditorAdapter>(sp =>
                decorateEditor(sp.GetRequiredService<TravelCut.ResolveBridge.MockResolveAdapter>())));
        }
        services.AddSingleton<McpIdempotencyCache>();
        services.AddSingleton<TravelCutMcpFacade>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TravelCutDbContext>().EnsureSchemaUpdated();
        }

        return new McpTestHost(root, provider, pipeline, configuration);
    }

    /// <summary>Projetos A (autorizável) e B, mídias sob 1-Bruto e sugestões Proposed.</summary>
    public async Task SeedAsync()
    {
        using var scope = _provider.CreateScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var media = scope.ServiceProvider.GetRequiredService<IMediaRepository>();
        var plans = scope.ServiceProvider.GetRequiredService<IEditPlanRepository>();

        await projects.SaveAsync(new Project { ProjectId = ProjectA, Name = "Viagem A", RootPath = Path.Combine(Root, "A") });
        await projects.SaveAsync(new Project { ProjectId = ProjectB, Name = "Viagem B", RootPath = Path.Combine(Root, "B") });

        await media.SaveBatchAsync(new[]
        {
            Asset(MediaA, ProjectA, "1-Bruto/clip1.mp4"),
            Asset(MediaA2, ProjectA, "1-Bruto/clip2.mp4"),
            Asset(MediaB, ProjectB, "1-Bruto/outro.mp4"),
        });

        await plans.SaveSuggestionsBatchAsync(new[]
        {
            Suggestion(SuggestionA, MediaA, 0, 5),
            Suggestion(SuggestionA2, MediaA2, 10, 14),
            Suggestion(SuggestionB, MediaB, 2, 6),
        });
    }

    public async Task<IReadOnlyList<EditDecisionView>> DecisionsAsync(Guid projectId)
    {
        using var scope = _provider.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<IEditPlanRepository>();
        var decisions = await plans.ListDecisionsByProjectAsync(projectId);
        return decisions.Select(d => new EditDecisionView(d.SuggestionId, d.Action)).ToList();
    }

    public sealed record EditDecisionView(Guid SuggestionId, string Action);

    private static MediaAsset Asset(Guid id, Guid projectId, string relative) => new()
    {
        MediaId = id,
        ProjectId = projectId,
        RelativePath = relative,
        ByteLength = 1024,
        LastWriteUtc = DateTime.UtcNow,
        Fingerprint = "fp-" + id.ToString("N")[..8],
        Kind = MediaKind.Video,
    };

    private static EditSuggestion Suggestion(Guid id, Guid mediaId, long startSeconds, long endSeconds) => new()
    {
        SuggestionId = id,
        MediaId = mediaId,
        SourceInterval = new TimeRange(startSeconds * TimeSpanTicksPerSecond, endSeconds * TimeSpanTicksPerSecond),
        Kind = SuggestionKind.Silence,
        Rationale = "silêncio de teste",
        Status = SuggestionStatus.Proposed,
    };

    // Mesma base usada pela persistência de sugestões (SuggestionStorageMapper.TicksPerSecond).
    private const long TimeSpanTicksPerSecond = TravelCut.Application.UseCases.SuggestionStorageMapper.TicksPerSecond;

    public void Dispose()
    {
        _provider.Dispose();
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Limpeza melhor esforço: o SQLite pode manter o arquivo aberto por um instante.
        }
    }
}

/// <summary>Job manager dublê: conta StartAsync, pode atrasar e pode falhar de forma controlada.</summary>
public sealed class TestPipelineJobManager : IPipelineJobManager
{
    private int _startCalls;
    private readonly List<StartPipelineRequest> _requests = new();
    private readonly object _gate = new();

    public Guid CurrentJobId { get; } = Guid.Parse("cccccccc-0000-4000-8000-00000000c0de");

    /// <summary>Atraso artificial para forçar sobreposição de chamadas concorrentes.</summary>
    public TimeSpan StartDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Quando definido, cada chamada a StartAsync lança esta exceção (ex.: "PIPELINE_IN_PROGRESS").</summary>
    public Func<int, Exception?>? FailOnCall { get; set; }

    public int StartCalls => Volatile.Read(ref _startCalls);

    public IReadOnlyList<StartPipelineRequest> Requests
    {
        get { lock (_gate) return _requests.ToList(); }
    }

    public async Task<Guid> StartAsync(StartPipelineRequest request, CancellationToken ct = default)
    {
        int call = Interlocked.Increment(ref _startCalls);
        lock (_gate) _requests.Add(request);
        if (StartDelay > TimeSpan.Zero)
        {
            await Task.Delay(StartDelay, ct);
        }
        var failure = FailOnCall?.Invoke(call);
        if (failure is not null)
        {
            throw failure;
        }
        return CurrentJobId;
    }

    public Task<PipelineStatusDto> GetStatusAsync(Guid projectId, CancellationToken ct = default) =>
        Task.FromResult(new PipelineStatusDto(
            JobId: CurrentJobId,
            ProjectId: projectId,
            State: "idle",
            Stages: new List<PipelineStageStatusDto>(),
            Media: new List<PipelineMediaStatusDto>(),
            CurrentMedia: null,
            Environment: null,
            StartedAtUtc: null,
            FinishedAtUtc: null));

    public bool Cancel(Guid jobId) => false;

    public Task<AnalysisEnvironmentDto> GetEnvironmentAsync(CancellationToken ct = default, bool forceRefresh = false) =>
        Task.FromResult(new AnalysisEnvironmentDto(
            FfmpegAvailable: false,
            FfmpegVersion: null,
            PythonAvailable: false,
            WhisperAvailable: false,
            WhisperEngine: null,
            WhisperModel: "large-v3",
            CudaAvailable: null,
            Reasons: new List<string> { "teste" }));

    public Task<MediaAnalysisDto?> GetMediaAnalysisAsync(Guid projectId, Guid mediaId, CancellationToken ct = default) =>
        Task.FromResult<MediaAnalysisDto?>(null);
}

/// <summary>Leitura do conteúdo JSON de um CallToolResult de texto.</summary>
public static class ToolJson
{
    public static string Text(ModelContextProtocol.Protocol.CallToolResult result)
    {
        var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        return block.Text;
    }

    public static JsonNode Parse(ModelContextProtocol.Protocol.CallToolResult result) =>
        JsonNode.Parse(Text(result))!;

    public static bool IsError(ModelContextProtocol.Protocol.CallToolResult result) => result.IsError ?? false;
}
