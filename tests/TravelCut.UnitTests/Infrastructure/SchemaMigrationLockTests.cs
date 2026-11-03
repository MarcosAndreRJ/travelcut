using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TravelCut.Infrastructure.Persistence;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

/// <summary>
/// M8: duas migrações simultâneas no mesmo arquivo (app e MCP) terminam sem erro, graças à trava
/// <c>&lt;db&gt;.migrate.lock</c>. Sem a trava, EnsureColumns (verifica e depois ADD COLUMN) corre e falha com "duplicate column".
/// </summary>
public sealed class SchemaMigrationLockTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tc_migrate_" + Guid.NewGuid().ToString("N"));

    public SchemaMigrationLockTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Melhor esforço: handles de teste podem estar liberando.
        }
    }

    private static string ConnectionString(string path) => new SqliteConnectionStringBuilder { DataSource = path }.ToString();

    private static TravelCutDbContext NewContext(string path) =>
        new(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(ConnectionString(path)).Options);

    /// <summary>Banco "antigo": schema completo e depois colunas novas removidas e marcador apagado, para forçar a migração.</summary>
    private static void CreateLegacyDatabase(string path)
    {
        using (var db = NewContext(path))
        {
            db.EnsureSchemaUpdated();
        }

        using var connection = new SqliteConnection(ConnectionString(path));
        connection.Open();
        foreach (var sql in new[]
                 {
                     "ALTER TABLE MediaMetadata DROP COLUMN IsVariableFrameRate;",
                     "ALTER TABLE MediaMetadata DROP COLUMN CameraModel;",
                     "ALTER TABLE MediaMetadata DROP COLUMN GpsLocation;",
                     "ALTER TABLE MediaMetadata DROP COLUMN RotationDegrees;",
                     "ALTER TABLE MediaMetadata DROP COLUMN MetadataStatus;",
                     "ALTER TABLE MediaAssets DROP COLUMN TagsJson;",
                     "DELETE FROM SchemaMarkers WHERE Name LIKE 'schema/%';",
                 })
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    [Fact]
    public async Task DuasMigracoesSimultaneasNoMesmoArquivo_TerminamSemDuplicateColumn()
    {
        for (int rodada = 0; rodada < 3; rodada++)
        {
            var path = Path.Combine(_dir, $"legacy-{rodada}.db");
            CreateLegacyDatabase(path);

            using var largada = new Barrier(2);
            var migracoes = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            {
                using var db = NewContext(path);
                largada.SignalAndWait();
                db.EnsureSchemaUpdated();
            })).ToArray();

            await Task.WhenAll(migracoes);

            Assert.True(TravelCutSchemaInspector.Inspect(path).IsReady, $"rodada {rodada}");
            Assert.False(File.Exists(path + TravelCutDbContext.MigrationLockSuffix), "a trava de migração deve ser removida ao fim");
        }
    }

    [Fact]
    public void BancoEmMemoria_NaoCriaArquivoDeTrava()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();
        using var db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(connection).Options);

        db.EnsureSchemaUpdated();

        Assert.Empty(Directory.GetFiles(_dir));
    }
}
