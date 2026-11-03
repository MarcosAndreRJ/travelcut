using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TravelCut.Infrastructure.Persistence;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

/// <summary>
/// Marcador de versão do schema (<c>schema/N</c>, último passo da migração) exigido pelo inspetor:
/// ausente → NOT_READY; maior que a conhecida → TOO_NEW. Leitura que falha (arquivo bloqueado) nunca lança.
/// </summary>
public sealed class SchemaVersionMarkerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tc_marker_" + Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(_dir, "travelcut.db");

    public SchemaVersionMarkerTests() => Directory.CreateDirectory(_dir);

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

    private string ConnectionString() => new SqliteConnectionStringBuilder { DataSource = DbPath }.ToString();

    private void MigrateWithDbContext()
    {
        using var db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>()
            .UseSqlite(ConnectionString()).Options);
        db.EnsureSchemaUpdated();
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(ConnectionString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void EnsureSchemaUpdated_GravaOMarcadorDaVersaoAtual()
    {
        Assert.Equal(1, TravelCutDbContext.CurrentSchemaVersion);

        MigrateWithDbContext();

        using var connection = new SqliteConnection(ConnectionString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SchemaMarkers WHERE Name = 'schema/1';";
        Assert.Equal(1L, Convert.ToInt64(cmd.ExecuteScalar()));
        Assert.True(TravelCutSchemaInspector.Inspect(DbPath).IsReady);
    }

    [Fact]
    public void BancoSemMarcadorDeVersao_NaoEhPronto_CodigoNotReady()
    {
        MigrateWithDbContext();
        Execute("DELETE FROM SchemaMarkers WHERE Name LIKE 'schema/%';");

        var r = TravelCutSchemaInspector.Inspect(DbPath);

        Assert.False(r.IsReady);
        Assert.Equal(SchemaReadinessCodes.NotReady, r.Code);
        Assert.Contains("marcador", r.Reason);
        Assert.DoesNotContain(_dir, r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarcadorDeVersaoMaiorQueAConhecida_EhTooNew_ComMensagemClara()
    {
        MigrateWithDbContext();
        Execute("INSERT INTO SchemaMarkers (Name, AppliedAtUtc) VALUES ('schema/99', '2030-01-01T00:00:00Z');");

        var r = TravelCutSchemaInspector.Inspect(DbPath);

        Assert.False(r.IsReady);
        Assert.Equal(SchemaReadinessCodes.TooNew, r.Code);
        Assert.Contains("banco de versão mais nova", r.Reason);
        Assert.DoesNotContain(_dir, r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureSchemaUpdated_BancoDeVersaoMaisNova_RecusaSemMigrar()
    {
        MigrateWithDbContext();
        Execute("INSERT INTO SchemaMarkers (Name, AppliedAtUtc) VALUES ('schema/99', '2030-01-01T00:00:00Z');");

        var ex = Assert.Throws<InvalidOperationException>(MigrateWithDbContext);
        Assert.Equal("SCHEMA_TOO_NEW", ex.Message);
    }

    [Fact]
    public void MarcadorDeVersaoIgualAConhecida_EhPronto()
    {
        MigrateWithDbContext();
        Execute("INSERT OR IGNORE INTO SchemaMarkers (Name, AppliedAtUtc) VALUES ('schema/1', '2030-01-01T00:00:00Z');");

        Assert.True(TravelCutSchemaInspector.Inspect(DbPath).IsReady);
    }

    [Fact]
    public void ArquivoBloqueadoPorOutroProcesso_NaoLanca_RetornaNaoPronto()
    {
        MigrateWithDbContext();
        // O pool do Microsoft.Data.Sqlite pode manter o arquivo aberto: libera antes de tomar o arquivo em exclusivo.
        SqliteConnection.ClearAllPools();
        using var bloqueio = new FileStream(DbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        SchemaReadiness? r = null;
        var ex = Record.Exception(() => r = TravelCutSchemaInspector.Inspect(DbPath));

        Assert.Null(ex);
        Assert.NotNull(r);
        Assert.False(r!.IsReady);
        Assert.NotNull(r.Reason);
        Assert.DoesNotContain(_dir, r.Reason, StringComparison.OrdinalIgnoreCase);
    }
}
