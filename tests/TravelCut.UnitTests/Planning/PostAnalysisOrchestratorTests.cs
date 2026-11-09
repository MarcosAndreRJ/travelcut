using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Application.Planning;
using TravelCut.Application.PostAnalysis;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Planning;
using TravelCut.Infrastructure.Repositories;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>Fila de proxies fake: registra starts, deduplica por job ativo e expõe o último item por mídia.</summary>
public sealed class FakeProxyJobService : IProxyJobService
{
    private readonly object _gate = new();
    private readonly Dictionary<(Guid, Guid), ProxyItemSnapshot> _items = new();
    private ProxyJobSnapshot? _active;

    public List<IReadOnlyList<ProxyJobItemPlan>> StartCalls { get; } = new();
    public bool ThrowOnStart { get; set; }

    public ProxyStartOutcome Start(Guid projectId, string projectRoot, IReadOnlyList<ProxyJobItemPlan> items)
    {
        lock (_gate)
        {
            if (ThrowOnStart) throw new InvalidOperationException("falha simulada do proxy");
            if (_active is not null) return new ProxyStartOutcome(true, _active);

            StartCalls.Add(items);
            var snaps = items.Select(i => new ProxyItemSnapshot(i.MediaId, i.SkipReason is null ? "pending" : "skipped", i.SkipReason)).ToList();
            foreach (var s in snaps) _items[(projectId, s.MediaId)] = s;
            var job = new ProxyJobSnapshot(Guid.NewGuid(), projectId, "queued", 0, snaps.Count, null, snaps);
            _active = job;
            return new ProxyStartOutcome(false, job);
        }
    }

    public ProxyJobSnapshot? GetJob(Guid jobId) => _active is { } a && a.JobId == jobId ? a : null;

    public ProxyItemSnapshot? GetLatestItem(Guid projectId, Guid mediaId)
    {
        lock (_gate) return _items.TryGetValue((projectId, mediaId), out var i) ? i : null;
    }
}

public sealed class FakeFfmpegPath : IFfmpegPathProvider
{
    public string? GetFfmpegPath() => "ffmpeg-fake";
}

/// <summary>Pipeline fake com falha controlada na leitura de análises (derruba a geração do plano).</summary>
public sealed class ThrowingPipelineAnalysis : IPipelineJobManager
{
    public FakePipelineAnalysis Inner { get; } = new();
    public bool Throw { get; set; }

    public Task<MediaAnalysisDto?> GetMediaAnalysisAsync(Guid projectId, Guid mediaId, CancellationToken ct = default) =>
        Throw ? throw new InvalidOperationException("falha simulada de análise") : Inner.GetMediaAnalysisAsync(projectId, mediaId, ct);

    public Task<Guid> StartAsync(StartPipelineRequest request, CancellationToken ct = default) => Inner.StartAsync(request, ct);
    public Task<PipelineStatusDto> GetStatusAsync(Guid projectId, CancellationToken ct = default) => Inner.GetStatusAsync(projectId, ct);
    public bool Cancel(Guid jobId) => Inner.Cancel(jobId);
    public Task<AnalysisEnvironmentDto> GetEnvironmentAsync(CancellationToken ct = default, bool forceRefresh = false) =>
        Inner.GetEnvironmentAsync(ct, forceRefresh);
}

/// <summary>F14.8 / I2b (P01): orquestrador pós-análise com SQLite real em memória, planner real e fila de proxies fake.</summary>
public abstract class PostAnalysisHostBase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<TravelCutDbContext> _options;
    private readonly string _root;
    protected readonly ServiceProvider _services;
    protected readonly FakeProxyJobService _proxy = new();
    protected readonly ThrowingPipelineAnalysis _analysis = new();
    protected readonly PostAnalysisOrchestrator _orchestrator;
    protected readonly Guid _projectId = TravelFixture.ProjectId;

    protected PostAnalysisHostBase(bool seed)
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options;
        using (var init = new TravelCutDbContext(_options)) init.Database.EnsureCreated();

        _root = Path.Combine(Path.GetTempPath(), "tc_postan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddScoped(_ => new TravelCutDbContext(_options));
        services.AddScoped<IProjectRepository, SqliteProjectRepository>();
        services.AddScoped<IMediaRepository, SqliteMediaRepository>();
        services.AddScoped<ITranscriptRepository, SqliteTranscriptRepository>();
        services.AddScoped<IEditPlanRepository, SqliteEditPlanRepository>();
        services.AddScoped<IPlanRevisionRepository, SqlitePlanRevisionRepository>();
        services.AddSingleton<IPlanArtifactStore, PlanArtifactStore>();
        services.AddSingleton<IFileExistence, FileSystemFileExistence>();
        services.AddSingleton<IChronologyService, ChronologyService>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPipelineJobManager>(_analysis);
        services.AddSingleton<IProxyJobService>(_proxy);
        services.AddSingleton<IFfmpegPathProvider, FakeFfmpegPath>();
        services.AddScoped<PlanningUseCases>();
        services.AddScoped(sp => new PreviewUseCases(
            sp.GetRequiredService<IProjectRepository>(), sp.GetRequiredService<IMediaRepository>(),
            sp.GetRequiredService<IFfmpegPathProvider>(), null!, null!,
            sp.GetRequiredService<IProxyJobService>(), NullLogger<PreviewUseCases>.Instance));
        _services = services.BuildServiceProvider();

        using (var scope = _services.CreateScope())
        {
            var projects = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
            projects.SaveAsync(new Project { ProjectId = _projectId, Name = "Viagem", RootPath = _root }).GetAwaiter().GetResult();
            if (seed)
            {
                var media = scope.ServiceProvider.GetRequiredService<IMediaRepository>();
                var suggestions = scope.ServiceProvider.GetRequiredService<IEditPlanRepository>();
                media.SaveBatchAsync(TravelFixture.Media()).GetAwaiter().GetResult();
                foreach (var m in TravelFixture.Media())
                {
                    var full = Path.Combine(_root, m.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.WriteAllText(full, "midia-de-teste");
                }
                foreach (var s in TravelFixture.Suggestions()) suggestions.SaveSuggestionsBatchAsync([s]).GetAwaiter().GetResult();
                foreach (var d in TravelFixture.Decisions()) suggestions.SaveDecisionAsync(d).GetAwaiter().GetResult();
                foreach (var (id, a) in TravelFixture.Analyses()) _analysis.Inner.Analyses[id] = a;
            }
        }

        _orchestrator = new PostAnalysisOrchestrator(
            _services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, NullLogger<PostAnalysisOrchestrator>.Instance);
    }

    public void Dispose()
    {
        _orchestrator.Dispose();
        _services.Dispose();
        _connection.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* limpeza melhor esforço */ }
    }

    protected async Task SetAutoProcessAsync(bool? value)
    {
        using var scope = _services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
        var p = (await repo.GetByIdAsync(_projectId))!;
        p.AutoProcess = value;
        await repo.SaveAsync(p);
    }

    protected async Task<PostAnalysisStatusDto> RunAsync()
    {
        await _orchestrator.ScheduleAsync(_projectId, Guid.NewGuid());
        return _orchestrator.GetStatus(_projectId);
    }

    protected async Task<EditPlanDto> LatestPlanAsync()
    {
        using var scope = _services.CreateScope();
        var outcome = await scope.ServiceProvider.GetRequiredService<PlanningUseCases>()
            .GetAsync(new GetPlanRequest(_projectId, null));
        Assert.True(outcome.Success, outcome.ErrorCode);
        return outcome.Value!;
    }

}

public class PostAnalysisOrchestratorTests : PostAnalysisHostBase
{
    public PostAnalysisOrchestratorTests() : base(seed: true) { }

    [Fact]
    public void Status_BeforeAnyRun_IsIdle()
    {
        var s = _orchestrator.GetStatus(_projectId);
        Assert.Equal("idle", s.State);
        Assert.Equal(0, s.Failures);
        Assert.Empty(s.Issues);
    }

    [Fact]
    public async Task Completed_GeneratesPlanAndQueuesProxy_WithHonestCounts()
    {
        var status = await RunAsync();

        Assert.Equal("completed", status.State);
        Assert.Null(status.Stage);
        Assert.Equal(0, status.Failures);
        Assert.Empty(status.Issues);
        Assert.NotNull(status.PlanRevision);
        Assert.NotNull(status.FinishedAtUtc);

        var plan = await LatestPlanAsync();
        Assert.Equal(plan.Revision, status.PlanRevision);
        Assert.Equal(plan.Summary.AutoCutsApplied, status.AutoCutsApplied);
        Assert.Equal(plan.Summary.PendingSuggestions, status.PendingReview);

        // Só a mídia sem metadados (reprodutibilidade desconhecida) precisa de proxy; as h264/mp4 não.
        var call = Assert.Single(_proxy.StartCalls);
        var item = Assert.Single(call);
        Assert.Equal(1, status.ProxiesQueued);
        Assert.EndsWith("04-sem-metadados.mp4", item.DisplayName);
        Assert.Null(item.SkipReason);
    }

    [Fact]
    public async Task AutoProcessDisabled_DoesNothing()
    {
        await SetAutoProcessAsync(false);

        var status = await RunAsync();

        Assert.Equal("skipped", status.State);
        Assert.Equal(PostAnalysisOrchestrator.ReasonAutoProcessDisabled, status.Reason);
        Assert.Empty(_proxy.StartCalls);
        Assert.Null(status.PlanRevision);
        using var scope = _services.CreateScope();
        var list = await scope.ServiceProvider.GetRequiredService<PlanningUseCases>().ListAsync(_projectId);
        Assert.Empty(list.Value!.Revisions);
    }

    [Fact]
    public async Task AutoProcessNullOrTrue_Runs()
    {
        await SetAutoProcessAsync(true);
        Assert.Equal("completed", (await RunAsync()).State);
    }

    [Fact]
    public async Task ProxyFailure_DoesNotPreventPlan()
    {
        _proxy.ThrowOnStart = true;

        var status = await RunAsync();

        Assert.Equal("completed_with_issues", status.State);
        Assert.NotNull(status.PlanRevision);
        Assert.Equal(1, status.Failures);
        Assert.Contains($"{PostAnalysisOrchestrator.IssueProxyFailed}:EXCEPTION", status.Issues);
        Assert.Equal(0, status.ProxiesQueued);
        Assert.True((await LatestPlanAsync()).Revision >= 1);
    }

    [Fact]
    public async Task PlanFailure_DoesNotPreventProxies()
    {
        _analysis.Throw = true;

        var status = await RunAsync();

        Assert.Equal("completed_with_issues", status.State);
        Assert.Null(status.PlanRevision);
        Assert.Equal(1, status.Failures);
        Assert.Contains(status.Issues, i => i.StartsWith(PostAnalysisOrchestrator.IssuePlanFailed, StringComparison.Ordinal));
        Assert.Equal(1, status.ProxiesQueued);
        Assert.Single(_proxy.StartCalls);
    }

    [Fact]
    public async Task TwoRuns_DoNotDuplicatePlanRevisionOrProxies()
    {
        var first = await RunAsync();
        var second = await RunAsync();

        Assert.Equal("completed", second.State);
        Assert.Equal(first.PlanRevision, second.PlanRevision);
        Assert.Equal(first.AutoCutsApplied, second.AutoCutsApplied);
        Assert.Equal(first.PendingReview, second.PendingReview);
        Assert.Single(_proxy.StartCalls);
        Assert.Equal(0, second.ProxiesQueued);

        using var scope = _services.CreateScope();
        var list = await scope.ServiceProvider.GetRequiredService<PlanningUseCases>()
            .ListAsync(_projectId);
        Assert.True(list.Success);
        Assert.Single(list.Value!.Revisions);
    }

    [Fact]
    public async Task ConcurrentSchedules_NeverRunInParallel_AndEndIdempotent()
    {
        var a = _orchestrator.ScheduleAsync(_projectId, Guid.NewGuid());
        var b = _orchestrator.ScheduleAsync(_projectId, Guid.NewGuid());
        await Task.WhenAll(a, b);

        var status = _orchestrator.GetStatus(_projectId);
        Assert.Equal("completed", status.State);
        Assert.Single(_proxy.StartCalls);
    }

    [Fact]
    public async Task OptionsGetSet_RoundTrip_DefaultIsTrue()
    {
        using var scope = _services.CreateScope();
        var planning = scope.ServiceProvider.GetRequiredService<PlanningUseCases>();

        var initial = await planning.GetAutomationOptionsAsync(new GetAutomationOptionsRequest(_projectId));
        Assert.True(initial.Value!.AutoProcess);
        Assert.True(initial.Value.IsDefault);

        var set = await planning.SetAutomationOptionsAsync(new SetAutomationOptionsRequest(_projectId, false));
        Assert.False(set.Value!.AutoProcess);
        Assert.False(set.Value.IsDefault);

        using var scope2 = _services.CreateScope();
        var again = await scope2.ServiceProvider.GetRequiredService<PlanningUseCases>()
            .GetAutomationOptionsAsync(new GetAutomationOptionsRequest(_projectId));
        Assert.False(again.Value!.AutoProcess);
    }

    [Fact]
    public async Task OptionsGetSet_UnknownProject_Fails()
    {
        using var scope = _services.CreateScope();
        var planning = scope.ServiceProvider.GetRequiredService<PlanningUseCases>();
        var r = await planning.GetAutomationOptionsAsync(new GetAutomationOptionsRequest(Guid.NewGuid()));
        Assert.Equal("PROJECT_NOT_FOUND", r.ErrorCode);
    }
}

/// <summary>Projeto sem mídia/sugestões: nada a processar.</summary>
public class PostAnalysisOrchestratorEmptyProjectTests : PostAnalysisHostBase
{
    public PostAnalysisOrchestratorEmptyProjectTests() : base(seed: false) { }

    [Fact]
    public async Task NoAnalysisResults_IsSkippedHonestly()
    {
        await _orchestrator.ScheduleAsync(TravelFixture.ProjectId, Guid.NewGuid());
        var s = _orchestrator.GetStatus(TravelFixture.ProjectId);

        Assert.Equal("skipped", s.State);
        Assert.Equal(PostAnalysisOrchestrator.ReasonNoAnalysisResults, s.Reason);
        Assert.Empty(_proxy.StartCalls);
    }
}
