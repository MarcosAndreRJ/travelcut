using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Editor;
using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Infrastructure.Editor;
using TravelCut.Infrastructure.Locks;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Planning;
using TravelCut.Infrastructure.Repositories;
using TravelCut.UnitTests.Planning;
using Xunit;

namespace TravelCut.UnitTests.Editor;

/// <summary>
/// Projeto real (SQLite em memória, EF) com a viagem de referência, mídias sob 1-Bruto e arquivos reais em pasta temporária.
/// Armazenamento de plano e de editor reais; apenas o adaptador é dublê.
/// </summary>
public sealed class EditorFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TravelCutDbContext _db;

    public string Root { get; }
    public SqliteMediaRepository Media { get; }
    public SqliteProjectRepository Projects { get; }
    public FakeEditorAdapter Adapter { get; }
    public FileEditorArtifactStore EditorStore { get; }
    public PlanningUseCases Planning { get; }
    public IProjectOperationLock Locks { get; }
    public EditorUseCases Editor { get; }

    public EditorFixture()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        Root = Path.Combine(Path.GetTempPath(), "tc_editor_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        Projects = new SqliteProjectRepository(_db);
        Media = new SqliteMediaRepository(_db);
        var suggestions = new SqliteEditPlanRepository(_db);
        var revisions = new SqlitePlanRevisionRepository(_db);
        var analysis = new FakePipelineAnalysis();
        var planStore = new PlanArtifactStore();
        var clock = new SteppingTimeProvider();

        Projects.SaveAsync(new Project { ProjectId = TravelFixture.ProjectId, Name = "Viagem Teste", RootPath = Root })
            .GetAwaiter().GetResult();

        // Mídias sob 1-Bruto (pasta de origem) e arquivos reais, para a validação de SOURCE_FILE_MISSING passar.
        var assets = TravelFixture.Media();
        foreach (var m in assets)
        {
            m.RelativePath = "1-Bruto/" + m.RelativePath;
            var full = Path.Combine(Root, m.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "midia-de-teste");
        }
        Media.SaveBatchAsync(assets).GetAwaiter().GetResult();

        foreach (var s in TravelFixture.Suggestions()) suggestions.SaveSuggestionsBatchAsync([s]).GetAwaiter().GetResult();
        foreach (var d in TravelFixture.Decisions()) suggestions.SaveDecisionAsync(d).GetAwaiter().GetResult();
        foreach (var (id, a) in TravelFixture.Analyses()) analysis.Analyses[id] = a;

        Planning = new PlanningUseCases(
            Projects, Media, suggestions, analysis, new ChronologyService(), revisions, planStore,
            new FileSystemFileExistence(), clock, NullLogger<PlanningUseCases>.Instance);

        Adapter = new FakeEditorAdapter();
        EditorStore = new FileEditorArtifactStore(NullLogger<FileEditorArtifactStore>.Instance);
        Locks = new FileProjectOperationLock();
        Editor = new EditorUseCases(
            Projects, Media, Planning, Adapter, EditorStore, new FileSystemFileExistence(), Locks, clock,
            NullLogger<EditorUseCases>.Instance);
    }

    /// <summary>Gera o plano (revisão 1) com as regras reais do planejador.</summary>
    public async Task<EditPlanDto> GeneratePlanAsync()
    {
        var result = await Planning.GenerateAsync(new GeneratePlanRequest(TravelFixture.ProjectId, null, null));
        Assert.True(result.Success, result.ErrorMessage);
        return result.Value!;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Pasta temporária: limpeza melhor esforço.
        }
    }
}
