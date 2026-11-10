using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Planning;
using TravelCut.Infrastructure.Repositories;
using Xunit;

namespace TravelCut.UnitTests.Planning;

/// <summary>
/// R3 (F14.7a): save manual, generate e decisao concorrentes sobre o mesmo projeto. Cada operacao usa o seu proprio
/// DbContext/conexao (como os escopos reais) sobre um SQLite em arquivo e o armazenamento de artefatos em disco, para
/// que a concorrencia seja real. O lock por projeto precisa serializar sem deadlock e sem deixar revisoes orfas.
/// </summary>
public sealed class PlanConcurrencyTests : IDisposable
{
    private static readonly TimeSpan IterationTimeout = TimeSpan.FromSeconds(60);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tc_r3_" + Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private readonly string _connectionString;
    private readonly PlanArtifactStore _store = new();
    private readonly FakePipelineAnalysis _analysis = new();
    private readonly List<TravelCutDbContext> _contexts = new();
    private static Guid ProjectId => TravelFixture.ProjectId;

    public PlanConcurrencyTests()
    {
        _root = Path.Combine(_dir, "projeto");
        Directory.CreateDirectory(_root);
        _connectionString = $"Data Source={Path.Combine(_dir, "r3.db")}";

        using (var init = NewContext()) init.Database.EnsureCreated();
        using var db = NewContext();
        new SqliteProjectRepository(db).SaveAsync(new Project { ProjectId = ProjectId, Name = "Viagem", RootPath = _root }).GetAwaiter().GetResult();
        new SqliteMediaRepository(db).SaveBatchAsync(TravelFixture.Media()).GetAwaiter().GetResult();
        var plan = new SqliteEditPlanRepository(db);
        foreach (var s in TravelFixture.Suggestions()) plan.SaveSuggestionsBatchAsync([s]).GetAwaiter().GetResult();
        foreach (var (id, analysis) in TravelFixture.Analyses()) _analysis.Analyses[id] = analysis;
    }

    private TravelCutDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connectionString).Options);

    /// <summary>Um PlanningUseCases com contexto proprio, como um escopo de requisicao.</summary>
    private PlanningUseCases NewUseCases()
    {
        var db = NewContext();
        lock (_contexts) _contexts.Add(db);
        return new PlanningUseCases(
            new SqliteProjectRepository(db), new SqliteMediaRepository(db), new SqliteEditPlanRepository(db), _analysis,
            new ChronologyService(), new SqlitePlanRevisionRepository(db), _store, new FileSystemFileExistence(),
            TimeProvider.System, NullLogger<PlanningUseCases>.Instance);
    }

    public void Dispose()
    {
        foreach (var c in _contexts) c.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { /* limpeza melhor esforco */ }
    }

    [Fact]
    public async Task R3_SaveGenerateAndDecision_50xConcurrent_NoDeadlockNoOrphanRevisions()
    {
        var first = await NewUseCases().GenerateAsync(new GeneratePlanRequest(ProjectId, null, null));
        Assert.True(first.Success, first.ErrorMessage);

        for (int i = 0; i < 50; i++)
        {
            var latest = (await NewUseCases().GetAsync(new GetPlanRequest(ProjectId, null))).Value!;
            // Tarefas criadas e iniciadas em paralelo, cada uma com seu escopo.
            var save = Task.Run(() => NewUseCases().SaveManualRevisionAsync(
                new SavePlanRevisionRequest(ProjectId, latest.Revision, latest, Author: "user")));
            var generate = Task.Run(() => NewUseCases().GenerateAsync(new GeneratePlanRequest(ProjectId, null, null)));
            double start = 10.0 + (i % 7) * 0.1;
            var decision = Task.Run(() => NewUseCases().SubmitDecisionAsync(
                new SubmitDecisionRequest(TravelFixture.S(1), "modify", "user", null, start, start + 1.0)));

            await Task.WhenAll(save, generate, decision).WaitAsync(IterationTimeout);

            var saved = await save;
            Assert.True(saved.Success || saved.ErrorCode == PlanningUseCases.PlanStaleBaseCode,
                $"iteracao {i}: save {saved.ErrorCode} {saved.ErrorMessage}");
            var generated = await generate;
            Assert.True(generated.Success, $"iteracao {i}: generate {generated.ErrorCode} {generated.ErrorMessage}");
            var decided = await decision;
            Assert.True(decided.Success, $"iteracao {i}: decisao {decided.ErrorCode}");
            Assert.Null(decided.Value!.PlanError);
        }

        // Sem revisoes orfas: numeros do indice contiguos 1..N, identicos aos arquivos gravados, todos legiveis e integros.
        using var verify = NewContext();
        var revisions = new SqlitePlanRevisionRepository(verify);
        var index = await revisions.ListAsync(ProjectId);
        var numbers = index.Select(e => e.Revision).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range(1, numbers.Count), numbers);
        Assert.Single(index.Select(e => e.PlanId).Distinct());

        var planId = index[0].PlanId;
        var onDisk = (await _store.ListRevisionNumbersAsync(_root, planId)).OrderBy(n => n).ToList();
        Assert.Equal(numbers, onDisk);

        var useCases = NewUseCases();
        foreach (var n in numbers)
        {
            var read = await useCases.GetAsync(new GetPlanRequest(ProjectId, n));
            Assert.True(read.Success, $"revisao {n}: {read.ErrorCode}");
            Assert.DoesNotContain(read.Value!.Validation.Issues, i => i.Code == PlanningUseCases.TamperedCode);
        }
    }
}

/// <summary>Pos-analise herda a TimeBase da revisao vigente em vez de reescolher pela moda das midias.</summary>
public sealed class PlanPostAnalysisTimeBaseTests : PostAnalysisHostBase
{
    public PlanPostAnalysisTimeBaseTests() : base(seed: true) { }

    [Fact]
    public async Task PostAnalysis_InheritsTimeBaseFromLatestRevision()
    {
        var explicitBase = new TimelineTimeBaseDto(1, 24, "24 fps");
        using (var scope = _services.CreateScope())
        {
            var seeded = await scope.ServiceProvider.GetRequiredService<PlanningUseCases>()
                .GenerateAsync(new GeneratePlanRequest(_projectId, null, explicitBase));
            Assert.True(seeded.Success, seeded.ErrorMessage);
            Assert.Equal(explicitBase, seeded.Value!.TimeBase);
        }

        var status = await RunAsync();

        Assert.Equal(0, status.Failures);
        using var check = _services.CreateScope();
        var planning = check.ServiceProvider.GetRequiredService<PlanningUseCases>();
        foreach (var info in (await planning.ListAsync(_projectId)).Value!.Revisions)
        {
            var plan = (await planning.GetAsync(new GetPlanRequest(_projectId, info.Revision))).Value!;
            Assert.Equal(explicitBase, plan.TimeBase);
        }
    }
}
