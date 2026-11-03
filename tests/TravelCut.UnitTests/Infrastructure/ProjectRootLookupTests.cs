using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.UseCases;
using TravelCut.Domain.Entities;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Repositories;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

/// <summary>
/// Busca de projeto por RootPath (M3): "k:\X\" e "K:\X" devem resolver para o mesmo ProjectId;
/// registros antigos gravados com outra caixa continuam sendo encontrados.
/// </summary>
public sealed class ProjectRootLookupTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TravelCutDbContext _db;
    private readonly string _base = Path.Combine(Path.GetTempPath(), "tc_lookup_" + Guid.NewGuid().ToString("N"));

    public ProjectRootLookupTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        try
        {
            if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true);
        }
        catch (IOException)
        {
            // Melhor esforço.
        }
    }

    [Fact]
    public async Task AbrirComBarraFinalEDriveEmOutraCaixa_ResolveOMesmoProjectId()
    {
        string root = Path.Combine(_base, "X");
        CreateFourFolders(root);
        var useCase = new OpenOrCreateProjectUseCase(
            new SqliteProjectRepository(_db),
            new SqliteMediaRepository(_db),
            new SqliteEditPlanRepository(_db),
            NullLogger<OpenOrCreateProjectUseCase>.Instance);

        // "c:\...\X\" (unidade minúscula, barra final) e "C:\...\x" (unidade maiúscula, pasta em outra caixa).
        string lowerDriveWithSlash = char.ToLowerInvariant(root[0]) + root[1..] + Path.DirectorySeparatorChar;
        string upperDriveOtherCase = char.ToUpperInvariant(root[0]) + Path.Combine(_base, "x")[1..];

        var first = await useCase.ExecuteAsync(lowerDriveWithSlash);
        var second = await useCase.ExecuteAsync(upperDriveOtherCase);

        Assert.True(first.Success, first.ErrorMessage);
        Assert.True(second.Success, second.ErrorMessage);
        Assert.True(first.IsNewProject);
        Assert.False(second.IsNewProject);
        Assert.Equal(first.Summary!.ProjectId, second.Summary!.ProjectId);
        Assert.Equal(1, await _db.Projects.CountAsync());

        // Gravado normalizado: sem barra final.
        string stored = (await _db.Projects.SingleAsync()).RootPath;
        Assert.False(stored.EndsWith(Path.DirectorySeparatorChar), stored);
        Assert.Equal(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), stored, ignoreCase: true);
    }

    [Fact]
    public async Task RegistroAntigoComOutraCaixa_AindaEEncontrado()
    {
        string root = Path.Combine(_base, "Legado");
        Directory.CreateDirectory(root);
        // Só unidade e última pasta mudam de caixa (ASCII): o restante do caminho é idêntico.
        string parent = Path.GetDirectoryName(root)!;
        var legacy = new Project
        {
            ProjectId = Guid.NewGuid(),
            Name = "Legado",
            RootPath = char.ToUpperInvariant(root[0]) + Path.Combine(parent, "LEGADO")[1..],
            CreatedAtUtc = DateTime.UtcNow,
            LastModifiedAtUtc = DateTime.UtcNow,
        };
        _db.Projects.Add(legacy);
        await _db.SaveChangesAsync();

        var repo = new SqliteProjectRepository(_db);
        string query = char.ToLowerInvariant(root[0]) + Path.Combine(parent, "legado")[1..] + Path.DirectorySeparatorChar;
        var found = await repo.GetByRootPathAsync(query);

        Assert.NotNull(found);
        Assert.Equal(legacy.ProjectId, found.ProjectId);
    }

    [Fact]
    public async Task SaveAsync_NormalizaRootPathDeProjetoNovo()
    {
        string root = Path.Combine(_base, "Novo");
        Directory.CreateDirectory(root);
        var repo = new SqliteProjectRepository(_db);
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            Name = "Novo",
            RootPath = root + Path.DirectorySeparatorChar + Path.DirectorySeparatorChar,
            CreatedAtUtc = DateTime.UtcNow,
            LastModifiedAtUtc = DateTime.UtcNow,
        };

        await repo.SaveAsync(project);

        Assert.Equal(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), (await _db.Projects.SingleAsync()).RootPath, ignoreCase: true);
    }

    private static void CreateFourFolders(string root)
    {
        foreach (var folder in TravelCut.Domain.Entities.Project.RequiredFourFolders)
        {
            Directory.CreateDirectory(Path.Combine(root, folder));
        }
    }
}
