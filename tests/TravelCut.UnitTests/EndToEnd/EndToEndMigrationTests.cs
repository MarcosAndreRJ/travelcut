using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Infrastructure.Persistence;
using Xunit;
using static TravelCut.UnitTests.EndToEnd.E2EScenarioData;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// Sem fixtures de mídia: é migração de banco pela composição real (não usa ffprobe, Whisper nem FFmpeg).
/// Cenário 5: banco legado (formato anterior ao marcador schema/1 e às revisões do plano, com TranscriptWords
/// de PK só em Seq) aberto pela composição. Esperado: EnsureSchemaUpdated faz backup "travelcut.db.bak-&lt;utc&gt;",
/// grava o marcador schema/1 e preserva os dados.
/// Montagem do legado: esquema de tabelas pelo próprio modelo EF (DDL de EnsureCreated) e, em seguida, DDL explícito
/// que rebaixa as partes que mudaram depois (TranscriptWords, PlanRevisions, SchemaMarkers).
/// </summary>
public sealed class EndToEndMigrationTests : IDisposable
{
    private readonly E2EWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task BancoLegado_AbertoPelaComposicao_FazBackupGravaMarcadorEPreservaDados()
    {
        string dbPath = _ws.DatabasePath;
        string dbDir = Path.GetDirectoryName(dbPath)!;
        string root = _ws.ProjectRoot("Viagem Legada");
        foreach (var folder in Project.RequiredFourFolders)
        {
            Directory.CreateDirectory(Path.Combine(root, folder));
        }
        var projectId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var transcriptId = Guid.NewGuid();
        BuildLegacyDatabase(dbPath, projectId, mediaId, transcriptId, root);

        Assert.False(TravelCutSchemaInspector.Inspect(dbPath).IsReady, "legado: sem marcador de versão ainda não é pronto");
        Assert.Empty(Directory.GetFiles(dbDir, "travelcut.db.bak-*"));

        using (var host = E2EHost.Start(dbPath, Happy()))
        {
            // Dados preservados, lidos pelo IPC.
            var opened = await host.Ok("project.open", new OpenProjectRequest(root, AutoCreateFolders: false));
            Assert.Equal("Viagem Legada", opened.GetProperty("project").GetProperty("name").GetString());
            Assert.Equal(projectId, opened.GetProperty("project").GetProperty("projectId").GetGuid());

            var media = await host.Ok("media.list", new { projectId });
            var assetList = media.ValueKind == JsonValueKind.Array ? media.EnumerateArray().ToList() : media.GetProperty("media").EnumerateArray().ToList();
            var asset = Assert.Single(assetList);
            Assert.Equal(mediaId, asset.GetProperty("mediaId").GetGuid());
        }

        // Backup feito antes da migração: nome "<db>.bak-<UTC>" e contém o estado legado com as 3 palavras.
        var backups = Directory.GetFiles(dbDir, "travelcut.db.bak-*");
        var backup = Assert.Single(backups);
        Assert.Matches(@"^travelcut\.db\.bak-\d{17}$", Path.GetFileName(backup));
        Assert.Equal(3, CountScalar(backup, "SELECT COUNT(*) FROM TranscriptWords"));

        // Depois da migração: marcador schema/1, inspeção pronta, PK composta e palavras preservadas.
        Assert.Equal(1, CountScalar(dbPath, "SELECT COUNT(*) FROM SchemaMarkers WHERE Name = 'schema/1'"));
        Assert.True(TravelCutSchemaInspector.Inspect(dbPath).IsReady);
        Assert.Equal(3, CountScalar(dbPath,
            "SELECT COUNT(*) FROM TranscriptWords w JOIN Transcripts t ON w.TranscriptId = t.TranscriptId"));
        // PK composta (TranscriptId, Seq): duas colunas com pk > 0.
        Assert.Equal(2, CountScalar(dbPath, "SELECT COUNT(*) FROM PRAGMA_TABLE_INFO('TranscriptWords') WHERE pk > 0"));
        Assert.Equal("Olá|pessoal|tudo", ReadText(dbPath, "SELECT group_concat(Text, '|') FROM (SELECT Text FROM TranscriptWords ORDER BY Seq)"));

        // Reabrir não migra de novo: sem novo backup e marcador continua único.
        using (var again = E2EHost.Start(dbPath, Happy()))
        {
            var ready = await again.Ok("project.open", new OpenProjectRequest(root, AutoCreateFolders: false));
            Assert.Equal(projectId, ready.GetProperty("project").GetProperty("projectId").GetGuid());
        }
        Assert.Single(Directory.GetFiles(dbDir, "travelcut.db.bak-*"));
        Assert.Equal(1, CountScalar(dbPath, "SELECT COUNT(*) FROM SchemaMarkers WHERE Name = 'schema/1'"));
    }

    [Fact]
    public void BancoComVersaoSchemaMaisNova_LancaSchemaTooNewENaoMigraParaTras()
    {
        string dbPath = _ws.DatabasePath;
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        var options = new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(connectionString).Options;
        using (var db = new TravelCutDbContext(options))
        {
            db.EnsureSchemaUpdated();
        }

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            Exec(connection, "INSERT INTO SchemaMarkers (Name, AppliedAtUtc) VALUES ('schema/999', '2026-11-03T00:00:00Z');");
        }

        var readiness = TravelCutSchemaInspector.Inspect(dbPath);
        Assert.False(readiness.IsReady);
        Assert.Equal(SchemaReadinessCodes.TooNew, readiness.Code);

        using var dbTooNew = new TravelCutDbContext(options);
        var ex = Assert.Throws<InvalidOperationException>(() => dbTooNew.EnsureSchemaUpdated());
        Assert.Equal("SCHEMA_TOO_NEW", ex.Message);
    }

    [Fact]
    public void BancoInexistenteOuCorrompido_InspectorRetornaNotReadySemLancar()
    {
        string nonExistentPath = Path.Combine(_ws.Base, "non-existent.db");
        var resMissing = TravelCutSchemaInspector.Inspect(nonExistentPath);
        Assert.False(resMissing.IsReady);
        Assert.Equal(SchemaReadinessCodes.NotReady, resMissing.Code);

        string corruptPath = Path.Combine(_ws.Base, "corrupted.db");
        File.WriteAllBytes(corruptPath, [0x00, 0xFF, 0x12, 0x34, 0xDE, 0xAD, 0xBE, 0xEF]);
        var resCorrupt = TravelCutSchemaInspector.Inspect(corruptPath);
        Assert.False(resCorrupt.IsReady);
        Assert.Equal(SchemaReadinessCodes.NotReady, resCorrupt.Code);
    }

    [Fact]
    public async Task ProjetoVazio_AbertoPelaComposicao_SemMidias_FuncionaCorretamente()
    {
        string dbPath = _ws.DatabasePath;
        string root = _ws.ProjectRoot("Viagem Vazia");
        foreach (var folder in Project.RequiredFourFolders)
        {
            Directory.CreateDirectory(Path.Combine(root, folder));
        }

        using var host = E2EHost.Start(dbPath, Happy());
        var opened = await host.Ok("project.open", new OpenProjectRequest(root, AutoCreateFolders: false));
        var projProp = opened.GetProperty("project");
        var projectId = projProp.GetProperty("projectId").GetGuid();
        Assert.Equal("Viagem Vazia", projProp.GetProperty("name").GetString());

        var mediaListJson = await host.Ok("media.list", new { projectId });
        var assets = mediaListJson.ValueKind == JsonValueKind.Array
            ? mediaListJson.EnumerateArray().ToList()
            : mediaListJson.GetProperty("media").EnumerateArray().ToList();
        Assert.Empty(assets);

        var suggestionsJson = await host.Ok("suggestions.list", new { projectId });
        var suggestions = suggestionsJson.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Empty(suggestions);

        var revisionsJson = await host.Ok("plan.list", new { projectId });
        var revisions = revisionsJson.ValueKind == JsonValueKind.Array
            ? revisionsJson.EnumerateArray().ToList()
            : revisionsJson.GetProperty("revisions").EnumerateArray().ToList();
        Assert.Empty(revisions);

        Assert.True(TravelCutSchemaInspector.Inspect(dbPath).IsReady);
    }

    [Fact]
    public async Task BancoLegado_ComPalavrasOrfas_PreservaEmQuarentenaSemPerderDados()
    {
        string dbPath = _ws.DatabasePath;
        string dbDir = Path.GetDirectoryName(dbPath)!;
        string root = _ws.ProjectRoot("Viagem Orfas");
        foreach (var folder in Project.RequiredFourFolders)
        {
            Directory.CreateDirectory(Path.Combine(root, folder));
        }
        var projectId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();
        var transcriptId = Guid.NewGuid();

        BuildLegacyDatabase(dbPath, projectId, mediaId, transcriptId, root);

        // Inserir palavra órfã na tabela legada antes da migração
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            Exec(connection, $"INSERT INTO TranscriptWords (Seq, StartSeconds, EndSeconds, Text, Confidence, TranscriptId) VALUES (999, 10.0, 11.0, 'orfa', 0.8, '{Guid.NewGuid()}');");
        }

        using (var host = E2EHost.Start(dbPath, Happy()))
        {
            var opened = await host.Ok("project.open", new OpenProjectRequest(root, AutoCreateFolders: false));
            Assert.Equal(projectId, opened.GetProperty("project").GetProperty("projectId").GetGuid());
        }

        // Tabela nova deve conter as 3 palavras válidas associadas ao transcriptId real
        Assert.Equal(3, CountScalar(dbPath, "SELECT COUNT(*) FROM TranscriptWords"));
        // Tabela de quarentena deve conter a palavra órfã preservada
        Assert.Equal(1, CountScalar(dbPath, "SELECT COUNT(*) FROM TranscriptWords_orphans"));
        Assert.Equal("orfa", ReadText(dbPath, "SELECT Text FROM TranscriptWords_orphans WHERE Seq = 999"));
    }

    private static void BuildLegacyDatabase(string dbPath, Guid projectId, Guid mediaId, Guid transcriptId, string root)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        var options = new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(connectionString).Options;

        // Dados e tabelas pelo modelo atual, sem EnsureSchemaUpdated (o legado não tem marcador).
        using (var db = new TravelCutDbContext(options))
        {
            db.Database.EnsureCreated();
            db.Projects.Add(new Project { ProjectId = projectId, Name = "Viagem Legada", RootPath = root });
            db.MediaAssets.Add(new MediaAsset
            {
                MediaId = mediaId,
                ProjectId = projectId,
                RelativePath = "1-Bruto/clip01.mp4",
                ByteLength = 4096,
                LastWriteUtc = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
                Fingerprint = "fp-legado",
                Kind = MediaKind.Video,
            });
            db.Transcripts.Add(new Transcript
            {
                TranscriptId = transcriptId,
                MediaId = mediaId,
                ModelId = "large-v3",
                Language = "pt",
                AudioDurationSeconds = 8,
                FullText = "Olá pessoal tudo",
                Words =
                [
                    new TranscriptWord { Seq = 1, StartSeconds = 0.0, EndSeconds = 0.5, Text = "Olá", Confidence = 0.9 },
                    new TranscriptWord { Seq = 2, StartSeconds = 0.5, EndSeconds = 1.0, Text = "pessoal", Confidence = 0.9 },
                    new TranscriptWord { Seq = 3, StartSeconds = 1.0, EndSeconds = 1.5, Text = "tudo", Confidence = 0.9 },
                ],
            });
            db.SaveChanges();
        }

        // Rebaixa ao formato anterior: sem revisões do plano, sem marcador e com TranscriptWords de PK só em Seq.
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        Exec(connection, "DROP TABLE IF EXISTS PlanRevisions;");
        Exec(connection, "DROP TABLE IF EXISTS SchemaMarkers;");
        Exec(connection, "ALTER TABLE TranscriptWords RENAME TO TranscriptWords_atual;");
        Exec(connection,
            "CREATE TABLE TranscriptWords (Seq INTEGER NOT NULL PRIMARY KEY, StartSeconds REAL NOT NULL, " +
            "EndSeconds REAL NOT NULL, Text TEXT NULL, Confidence REAL NOT NULL, TranscriptId TEXT NOT NULL);");
        Exec(connection,
            "INSERT INTO TranscriptWords (Seq, StartSeconds, EndSeconds, Text, Confidence, TranscriptId) " +
            "SELECT Seq, StartSeconds, EndSeconds, Text, Confidence, TranscriptId FROM TranscriptWords_atual;");
        Exec(connection, "DROP TABLE TranscriptWords_atual;");
    }

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long CountScalar(string dbPath, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var value = Convert.ToInt64(cmd.ExecuteScalar());
        connection.Close();
        SqliteConnection.ClearPool(connection);
        return value;
    }

    private static string ReadText(string dbPath, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString());
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var value = cmd.ExecuteScalar()?.ToString() ?? string.Empty;
        connection.Close();
        SqliteConnection.ClearPool(connection);
        return value;
    }
}
