using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TravelCut.Infrastructure.Persistence;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

/// <summary>
/// Verificação de schema usada pelo servidor MCP sem --migrate-database: somente leitura, sem criar o arquivo.
/// </summary>
public sealed class SchemaInspectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tc_schema_" + Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(_dir, "travelcut.db");

    public SchemaInspectorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Melhor esforço: conexões de teste podem estar liberando o arquivo.
        }
    }

    [Fact]
    public void BancoInexistente_NaoEhProntoENaoCriaOArquivo()
    {
        var r = TravelCutSchemaInspector.Inspect(DbPath);

        Assert.False(r.IsReady);
        Assert.Equal("banco inexistente", r.Reason);
        Assert.False(File.Exists(DbPath));
    }

    [Fact]
    public void BancoVazio_NaoEhPronto()
    {
        File.WriteAllBytes(DbPath, []);

        var r = TravelCutSchemaInspector.Inspect(DbPath);

        Assert.False(r.IsReady);
        Assert.Contains("tabela", r.Reason);
    }

    [Fact]
    public void SchemaCompletoAplicadoPeloDbContext_EhPronto()
    {
        using (var db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>()
                   .UseSqlite(new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString()).Options))
        {
            db.EnsureSchemaUpdated();
        }

        var r = TravelCutSchemaInspector.Inspect(DbPath);

        Assert.True(r.IsReady, r.Reason);
        Assert.Null(r.Reason);
    }

    [Fact]
    public void TabelaObrigatoriaAusente_NaoEhPronto_EMotivoNaoTemCaminho()
    {
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString()))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "CREATE TABLE Projects (ProjectId TEXT PRIMARY KEY);";
            cmd.ExecuteNonQuery();
        }

        var r = TravelCutSchemaInspector.Inspect(DbPath);

        Assert.False(r.IsReady);
        Assert.Equal("tabela MediaAssets ausente", r.Reason);
        Assert.DoesNotContain(_dir, r.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
