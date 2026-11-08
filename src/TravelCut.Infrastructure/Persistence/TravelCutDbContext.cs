using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Entities.Editorial;
using TravelCut.Domain.ValueObjects;
using TravelCut.Infrastructure.Locks;
using TravelCut.Infrastructure.Planning;

namespace TravelCut.Infrastructure.Persistence;

public class TravelCutDbContext : DbContext
{
    /// <summary>Coluna TEXT (JSON) com as tags de MediaAsset.</summary>
    public const string TagsColumnName = "TagsJson";

    /// <summary>Chave estrangeira (shadow property) de TranscriptWord para Transcript.</summary>
    public const string TranscriptWordForeignKey = "TranscriptId";

    private const string LegacyTranscriptWordsSuffix = "_legacy_pk_seq";

    /// <summary>Tabela de índice das revisões do plano (ver <see cref="PlanRevisionRecord"/>).</summary>
    public const string PlanRevisionsTable = "PlanRevisions";

    /// <summary>Índice único (SuggestionId, Version) de EditDecisions. Mesmo nome usado pelo EF no modelo.</summary>
    public const string EditDecisionVersionIndex = "IX_EditDecisions_SuggestionId_Version";

    /// <summary>
    /// DDL idempotente para bancos JÁ existentes: EnsureCreated não altera um banco com tabelas.
    /// Deve coincidir com o modelo EF (nomes de colunas, PK composta e índices).
    /// </summary>
    private static readonly string[] PlanRevisionsDdl =
    [
        """
        CREATE TABLE IF NOT EXISTS PlanRevisions (
            PlanId TEXT NOT NULL,
            Revision INTEGER NOT NULL,
            ProjectId TEXT NOT NULL,
            BaseRevision INTEGER NULL,
            CreatedAtUtc TEXT NOT NULL,
            Author TEXT NOT NULL,
            PlanFingerprint TEXT NOT NULL,
            IsValid INTEGER NOT NULL,
            ClipCount INTEGER NOT NULL,
            TimelineDurationFrames INTEGER NOT NULL,
            RelativeJsonPath TEXT NOT NULL,
            InputsFingerprint TEXT NULL,
            PRIMARY KEY (PlanId, Revision)
        );
        """,
        "CREATE INDEX IF NOT EXISTS IX_PlanRevisions_ProjectId ON PlanRevisions (ProjectId);",
        "CREATE UNIQUE INDEX IF NOT EXISTS IX_PlanRevisions_ProjectId_Revision ON PlanRevisions (ProjectId, Revision);",
    ];

    public DbSet<Project> Projects => Set<Project>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<MediaMetadata> MediaMetadata => Set<MediaMetadata>();
    public DbSet<Transcript> Transcripts => Set<Transcript>();
    public DbSet<TranscriptWord> TranscriptWords => Set<TranscriptWord>();
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<EditSuggestion> EditSuggestions => Set<EditSuggestion>();
    public DbSet<EditDecision> EditDecisions => Set<EditDecision>();
    public DbSet<EditPlan> EditPlans => Set<EditPlan>();
    public DbSet<TimelineClip> TimelineClips => Set<TimelineClip>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<PlanRevisionRecord> PlanRevisions => Set<PlanRevisionRecord>();
    public DbSet<SceneClassificationEntry> SceneClassifications => Set<SceneClassificationEntry>();
    public DbSet<EditorialTransition> EditorialTransitions => Set<EditorialTransition>();

    public TravelCutDbContext(DbContextOptions<TravelCutDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Project
        modelBuilder.Entity<Project>(b =>
        {
            b.HasKey(p => p.ProjectId);
            b.HasIndex(p => p.RootPath).IsUnique();
            b.Property(p => p.Name).IsRequired().HasMaxLength(255);
            b.Property(p => p.RootPath).IsRequired().HasMaxLength(1024);
        });

        // MediaAsset
        modelBuilder.Entity<MediaAsset>(b =>
        {
            b.HasKey(m => m.MediaId);
            b.HasIndex(m => new { m.ProjectId, m.RelativePath }).IsUnique();
            b.Property(m => m.RelativePath).IsRequired().HasMaxLength(1024);
            b.Property(m => m.Fingerprint).HasMaxLength(128);

            // Tags como JSON em TEXT (coluna TagsJson). Comparador por conteúdo para detectar alterações.
            b.Property(m => m.Tags)
             .HasColumnName(TagsColumnName)
             .HasColumnType("TEXT")
             .IsRequired()
             .HasConversion(
                 new ValueConverter<List<string>, string>(
                     v => SerializeTags(v),
                     s => DeserializeTags(s)),
                 new ValueComparer<List<string>>(
                     (a, b2) => (a == null && b2 == null) || (a != null && b2 != null && a.SequenceEqual(b2)),
                     v => v.Aggregate(0, (h, t) => HashCode.Combine(h, t.GetHashCode())),
                     v => v.ToList()));

            b.HasOne(m => m.Metadata)
             .WithOne()
             .HasForeignKey<MediaMetadata>(md => md.MediaId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // MediaMetadata
        modelBuilder.Entity<MediaMetadata>(b =>
        {
            b.HasKey(md => md.MediaId);
            b.Property(md => md.Codec).HasMaxLength(64);
            b.Property(md => md.SourceDateEvidence).HasMaxLength(255);
            b.Property(md => md.MetadataStatus).HasMaxLength(32).IsRequired();

            b.Property(md => md.TimeBase)
             .HasConversion(
                 tb => $"{tb.Numerator}:{tb.Denominator}",
                 s => ParseTimeBase(s)
             );
        });

        // Transcript
        modelBuilder.Entity<Transcript>(b =>
        {
            b.HasKey(t => t.TranscriptId);
            b.HasIndex(t => t.MediaId);
            b.Property(t => t.ModelId).HasMaxLength(64);
            b.Property(t => t.Language).HasMaxLength(16);

            b.HasMany(t => t.Words)
             .WithOne()
             .OnDelete(DeleteBehavior.Cascade);
        });

        // TranscriptWord
        // TranscriptWord: chave composta (TranscriptId, Seq). Com PK só em Seq, duas mídias com transcrição colidiam.
        modelBuilder.Entity<TranscriptWord>(b =>
        {
            b.Property<Guid>(TranscriptWordForeignKey);
            b.HasKey(TranscriptWordForeignKey, nameof(TranscriptWord.Seq));
            b.Property(w => w.Text).HasMaxLength(255);
        });

        // Scene
        modelBuilder.Entity<Scene>(b =>
        {
            b.HasKey(s => s.SceneId);
            b.HasIndex(s => s.MediaId);
            b.Property(s => s.SourceInterval)
             .HasConversion(
                 r => $"{r.StartTicks}:{r.EndTicks}",
                 s => ParseTimeRange(s)
             );
        });

        // EditSuggestion
        modelBuilder.Entity<EditSuggestion>(b =>
        {
            b.HasKey(s => s.SuggestionId);
            b.HasIndex(s => s.MediaId);
            b.Property(s => s.SuggestedAction).HasMaxLength(64);
            b.Property(s => s.ConfidenceBand).HasMaxLength(32);
            b.Property(s => s.Author).HasMaxLength(64);

            b.Property(s => s.SourceInterval)
             .HasConversion(
                 r => $"{r.StartTicks}:{r.EndTicks}",
                 s => ParseTimeRange(s)
             );
        });

        // EditDecision
        modelBuilder.Entity<EditDecision>(b =>
        {
            b.HasKey(d => d.DecisionId);
            b.HasIndex(d => d.SuggestionId);
            // Decisões são append-only: uma única Version por sugestão (garante a ordem do histórico).
            b.HasIndex(d => new { d.SuggestionId, d.Version }).IsUnique().HasDatabaseName(EditDecisionVersionIndex);
            b.Property(d => d.Action).HasMaxLength(64);
            b.Property(d => d.Reviewer).HasMaxLength(64);
            b.Property(d => d.IsRedo).HasDefaultValue(false);
        });

        // EditPlan
        modelBuilder.Entity<EditPlan>(b =>
        {
            b.HasKey(p => p.PlanId);
            b.HasIndex(p => p.ProjectId);

            b.Property(p => p.TimelineTimeBase)
             .HasConversion(
                 tb => $"{tb.Numerator}:{tb.Denominator}",
                 s => ParseTimeBase(s)
             );

            b.HasMany(p => p.Clips)
             .WithOne()
             .HasForeignKey(c => c.PlanId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // TimelineClip
        modelBuilder.Entity<TimelineClip>(b =>
        {
            b.HasKey(c => c.ClipId);
            b.HasIndex(c => c.PlanId);
            b.Property(c => c.TrackId).HasMaxLength(32);

            b.Property(c => c.SourceInterval)
             .HasConversion(
                 r => $"{r.StartTicks}:{r.EndTicks}",
                 s => ParseTimeRange(s)
             );

            b.Property(c => c.SpeedRatio)
             .HasConversion(
                 tb => $"{tb.Numerator}:{tb.Denominator}",
                 s => ParseTimeBase(s)
             );
        });

        // ProcessingJob
        modelBuilder.Entity<ProcessingJob>(b =>
        {
            b.HasKey(j => j.JobId);
            b.HasIndex(j => j.ProjectId);
            b.Property(j => j.CurrentStepDescription).HasMaxLength(512);
        });

        // PlanRevisions: índice imutável (uma linha por revisão). (ProjectId, Revision) é único.
        modelBuilder.Entity<PlanRevisionRecord>(b =>
        {
            b.ToTable(PlanRevisionsTable);
            b.HasKey(r => new { r.PlanId, r.Revision });
            b.HasIndex(r => r.ProjectId);
            b.HasIndex(r => new { r.ProjectId, r.Revision }).IsUnique();
            b.Property(r => r.Author).HasMaxLength(64).IsRequired();
            b.Property(r => r.PlanFingerprint).HasMaxLength(128).IsRequired();
            b.Property(r => r.RelativeJsonPath).HasMaxLength(1024).IsRequired();
        });

        // SceneClassificationEntry (F14.4)
        modelBuilder.Entity<SceneClassificationEntry>(b =>
        {
            b.HasKey(s => s.EntryId);
            b.HasIndex(s => new { s.ProjectId, s.MediaId });
            b.Property(s => s.Notes).HasMaxLength(1024);
        });

        // EditorialTransition (F14.4)
        modelBuilder.Entity<EditorialTransition>(b =>
        {
            b.HasKey(t => t.TransitionId);
            b.HasIndex(t => t.ProjectId);
            b.HasIndex(t => new { t.MediaIdA, t.MediaIdB });
            b.Property(t => t.Rationale).HasMaxLength(1024);
        });
    }

    private static RationalTimeBase ParseTimeBase(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return RationalTimeBase.Fps30;
        var parts = s.Split(':');
        if (parts.Length == 2 && long.TryParse(parts[0], out var num) && long.TryParse(parts[1], out var den))
        {
            return new RationalTimeBase(num, den);
        }
        return RationalTimeBase.Fps30;
    }

    private static TimeRange ParseTimeRange(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new TimeRange(0, 0);
        var parts = s.Split(':');
        if (parts.Length == 2 && long.TryParse(parts[0], out var start) && long.TryParse(parts[1], out var end))
        {
            return new TimeRange(start, end);
        }
        return new TimeRange(0, 0);
    }

    /// <summary>Versão do schema que este build conhece. Gravada como marcador <c>schema/N</c> como ÚLTIMO passo da migração.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Prefixo do marcador de versão em SchemaMarkers (ex.: <c>schema/1</c>).</summary>
    public const string SchemaMarkerPrefix = "schema/";

    /// <summary>Sufixo do arquivo de trava da migração, ao lado do banco (<c>&lt;db&gt;.migrate.lock</c>).</summary>
    public const string MigrationLockSuffix = ".migrate.lock";

    /// <summary>Espera máxima pela trava de migração, entre processos, antes de falhar com SCHEMA_MIGRATION_IN_PROGRESS.</summary>
    public static readonly TimeSpan MigrationLockWait = TimeSpan.FromSeconds(30);

    /// <summary>Tabela de quarentena das palavras legadas sem Transcript válido (preservadas, nunca descartadas).</summary>
    public const string TranscriptWordOrphansTable = "TranscriptWords_orphans";

    public void EnsureSchemaUpdated(ILogger? logger = null)
    {
        // A conexão pertence ao EF Core: não é descartada aqui (Dispose a fecharia para o contexto).
        var connection = Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            connection.Open();
        }

        // Uma migração por vez no mesmo arquivo: app e MCP podem abrir o mesmo banco ao mesmo tempo.
        using var migrationLock = AcquireMigrationLock(connection);
        // Banco gravado por um build mais novo: não migrar "para trás" (o app antigo poderia corromper o schema novo).
        if (ReadHighestSchemaVersion(connection) is int stored && stored > CurrentSchemaVersion)
        {
            throw new InvalidOperationException("SCHEMA_TOO_NEW");
        }
        ApplyMigrations(connection, logger);

        // Último passo: o marcador só existe quando todas as migrações anteriores (inclusive o backfill) concluíram.
        WriteSchemaMarker(connection);
    }

    /// <summary>Trava de migração ao lado do arquivo do banco. Bancos em memória não têm arquivo e não são travados.</summary>
    private static FileStream? AcquireMigrationLock(System.Data.Common.DbConnection connection)
    {
        string dataSource = connection.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:" || !File.Exists(dataSource))
        {
            return null;
        }

        var lockPath = Path.GetFullPath(dataSource) + MigrationLockSuffix;
        return ExclusiveFileLock.WaitExclusive(lockPath, MigrationLockWait)
            ?? throw new InvalidOperationException("SCHEMA_MIGRATION_IN_PROGRESS");
    }

    /// <summary>Maior versão <c>schema/N</c> registrada; null se não houver SchemaMarkers ou marcador de versão.</summary>
    private static int? ReadHighestSchemaVersion(System.Data.Common.DbConnection connection)
    {
        using var probe = connection.CreateCommand();
        probe.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = 'SchemaMarkers';";
        if (probe.ExecuteScalar() is null) return null;

        using var read = connection.CreateCommand();
        read.CommandText = "SELECT Name FROM SchemaMarkers WHERE Name LIKE 'schema/%';";
        using var reader = read.ExecuteReader();
        int? highest = null;
        while (reader.Read())
        {
            if (int.TryParse(reader.GetString(0).AsSpan(SchemaMarkerPrefix.Length), out var v) && (highest is null || v > highest))
                highest = v;
        }
        return highest;
    }

    /// <summary>Grava o marcador da versão atual (idempotente). Cria SchemaMarkers se ainda não existir.</summary>
    private static void WriteSchemaMarker(System.Data.Common.DbConnection connection)
    {
        ExecuteNonQuery(connection, null,
            "CREATE TABLE IF NOT EXISTS SchemaMarkers (Name TEXT NOT NULL PRIMARY KEY, AppliedAtUtc TEXT NOT NULL);");

        using var mark = connection.CreateCommand();
        mark.CommandText = "INSERT OR IGNORE INTO SchemaMarkers (Name, AppliedAtUtc) VALUES ($name, $at);";
        var name = mark.CreateParameter(); name.ParameterName = "$name"; name.Value = SchemaMarkerPrefix + CurrentSchemaVersion; mark.Parameters.Add(name);
        var at = mark.CreateParameter(); at.ParameterName = "$at"; at.Value = DateTime.UtcNow.ToString("O"); mark.Parameters.Add(at);
        mark.ExecuteNonQuery();
    }

    private void ApplyMigrations(System.Data.Common.DbConnection connection, ILogger? logger)
    {
        MigrateLegacyTranscriptWords(connection, logger);
        Database.EnsureCreated();

        // Bancos existentes não recebem tabelas novas via EnsureCreated: criação idempotente da tabela do plano.
        foreach (var statement in PlanRevisionsDdl)
        {
            using var ddl = connection.CreateCommand();
            ddl.CommandText = statement;
            ddl.ExecuteNonQuery();
        }

        // Tabelas da F14.4 (Classificação de Cenas e Transições Editoriais)
        var f144Statements = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS SceneClassifications (
                EntryId TEXT NOT NULL PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                MediaId TEXT NOT NULL,
                StartSeconds REAL NOT NULL,
                EndSeconds REAL NOT NULL,
                IsFullVideo INTEGER NOT NULL,
                Environment INTEGER NOT NULL,
                Interaction INTEGER NOT NULL,
                Framing INTEGER NOT NULL,
                Movement INTEGER NOT NULL,
                EditorialProfile INTEGER NOT NULL,
                Notes TEXT,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
            "CREATE INDEX IF NOT EXISTS IX_SceneClassifications_Project_Media ON SceneClassifications (ProjectId, MediaId);",
            """
            CREATE TABLE IF NOT EXISTS EditorialTransitions (
                TransitionId TEXT NOT NULL PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                MediaIdA TEXT NOT NULL,
                MediaIdB TEXT NOT NULL,
                TransitionType INTEGER NOT NULL,
                DurationSeconds REAL NOT NULL,
                Preset INTEGER NOT NULL,
                Status INTEGER NOT NULL,
                Origin TEXT NOT NULL,
                Rationale TEXT NOT NULL,
                IsAdjacentOnTimeline INTEGER NOT NULL,
                InsufficientHandles INTEGER NOT NULL,
                HandleWarningMessage TEXT,
                AvailableHandleFramesA REAL NOT NULL,
                AvailableHandleFramesB REAL NOT NULL,
                TimelineClipIdA TEXT,
                TimelineClipIdB TEXT,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );
            """,
            "CREATE INDEX IF NOT EXISTS IX_EditorialTransitions_ProjectId ON EditorialTransitions (ProjectId);",
            "CREATE INDEX IF NOT EXISTS IX_EditorialTransitions_MediaA_B ON EditorialTransitions (MediaIdA, MediaIdB);"
        };

        foreach (var statement in f144Statements)
        {
            using var ddl = connection.CreateCommand();
            ddl.CommandText = statement;
            ddl.ExecuteNonQuery();
        }

        // Revisões gravadas antes do fingerprint de entradas: coluna aditiva (NULL = revisão antiga, PLAN_OUTDATED não emitido).
        EnsureColumns(connection, "PlanRevisions", new (string Name, string Definition)[]
        {
            ("InputsFingerprint", "TEXT NULL")
        });

        // MediaMetadata: colunas de versões anteriores + status honesto da análise.
        // Linhas antigas recebem 'not_requested' e são reanalisadas pelo próximo scan (valores antigos podiam ser estimados).
        EnsureColumns(connection, "MediaMetadata", new (string Name, string Definition)[]
        {
            ("IsVariableFrameRate", "INTEGER NOT NULL DEFAULT 0"),
            ("CameraModel", "TEXT"),
            ("GpsLocation", "TEXT"),
            ("RotationDegrees", "INTEGER NOT NULL DEFAULT 0"),
            ("MetadataStatus", "TEXT NOT NULL DEFAULT 'not_requested'")
        });

        // MediaAssets: tags do usuário em JSON.
        EnsureColumns(connection, "MediaAssets", new (string Name, string Definition)[]
        {
            (TagsColumnName, "TEXT NOT NULL DEFAULT '[]'")
        });

        // EditDecisions: Version única por sugestão. Duplicatas legadas são renumeradas antes do índice único.
        // Tabela ausente (banco muito antigo, que EnsureCreated não completa): nada a fazer aqui.
        if (ReadTableColumns(connection, "EditDecisions").Count == 0) return;

        // Colunas aditivas (ADD COLUMN, sem reescrita): base da revisão e intervalo de origem decidido (ms).
        // IsRedo precisa existir antes da renumeração (que lê a entidade pelo EF); o backfill dele vem depois.
        EnsureColumns(connection, "EditDecisions", new (string Name, string Definition)[]
        {
            ("Version", "INTEGER NOT NULL DEFAULT 1"),
            ("BaseRevision", "INTEGER NULL"),
            ("SourceStartMs", "INTEGER NULL"),
            ("SourceEndMs", "INTEGER NULL"),
            ("IsRedo", "INTEGER NOT NULL DEFAULT 0")
        });

        // 1) Version única primeiro: em bancos antigos todas as linhas têm Version=1, e o backfill depende da ordem real.
        RenumberDuplicateEditDecisionVersions(connection);
        using (var index = connection.CreateCommand())
        {
            index.CommandText =
                $"CREATE UNIQUE INDEX IF NOT EXISTS {EditDecisionVersionIndex} ON EditDecisions (SuggestionId, Version);";
            index.ExecuteNonQuery();
        }

        // 2) Backfill único de IsRedo (redos antigos = ação original + "refeito" logo após um undo), atômico com o marcador:
        //    se o app cair no meio, nada fica marcado e a próxima inicialização tenta de novo; depois do marcador, nunca mais
        //    roda (não reinterpretar notas digitadas pelo usuário).
        BackfillLegacyRedoMarkersOnce(connection);
    }

    private const string IsRedoBackfillMarker = "EditDecisions.IsRedo.backfill/1";

    private static void BackfillLegacyRedoMarkersOnce(System.Data.Common.DbConnection connection)
    {
        ExecuteNonQuery(connection, null,
            "CREATE TABLE IF NOT EXISTS SchemaMarkers (Name TEXT NOT NULL PRIMARY KEY, AppliedAtUtc TEXT NOT NULL);");

        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM SchemaMarkers WHERE Name = $name;";
            var p = check.CreateParameter();
            p.ParameterName = "$name";
            p.Value = IsRedoBackfillMarker;
            check.Parameters.Add(p);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0) return;
        }

        const string candidates = """
            FROM EditDecisions
            WHERE Notes = 'refeito' AND IsRedo = 0 AND Version > 1
              AND EXISTS (SELECT 1 FROM EditDecisions p
                          WHERE p.SuggestionId = EditDecisions.SuggestionId
                            AND p.Version = EditDecisions.Version - 1
                            AND p.Action = 'undo')
            """;
        long toMark;
        using (var count = connection.CreateCommand())
        {
            count.CommandText = "SELECT COUNT(*) " + candidates + ";";
            toMark = Convert.ToInt64(count.ExecuteScalar());
        }
        // Reescreve dados de decisão do usuário: backup antes, como nas demais migrações que alteram linhas.
        if (toMark > 0) BackupDatabaseFile(connection);

        using var tx = connection.BeginTransaction();
        ExecuteNonQuery(connection, tx,
            "UPDATE EditDecisions SET IsRedo = 1 WHERE rowid IN (SELECT rowid " + candidates + ");");
        using (var mark = connection.CreateCommand())
        {
            mark.Transaction = tx;
            mark.CommandText = "INSERT INTO SchemaMarkers (Name, AppliedAtUtc) VALUES ($name, $at);";
            var name = mark.CreateParameter(); name.ParameterName = "$name"; name.Value = IsRedoBackfillMarker; mark.Parameters.Add(name);
            var at = mark.CreateParameter(); at.ParameterName = "$at"; at.Value = DateTime.UtcNow.ToString("O"); mark.Parameters.Add(at);
            mark.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// Para cada sugestão com Version repetida, renumera TODAS as suas decisões de 1..n na ordem (TimestampUtc, DecisionId).
    /// Idempotente: sem duplicatas não altera nada. Roda antes de criar o índice único.
    /// </summary>
    private void RenumberDuplicateEditDecisionVersions(System.Data.Common.DbConnection connection)
    {
        var decisions = EditDecisions.ToList();
        var duplicated = decisions
            .GroupBy(d => d.SuggestionId)
            .Where(g => g.Select(d => d.Version).Distinct().Count() != g.Count())
            .ToList();
        if (duplicated.Count == 0) return;

        // Reescreve dados de decisão do usuário: copia o banco antes (mesma política da migração de TranscriptWords).
        BackupDatabaseFile(connection);

        var changed = false;
        foreach (var group in duplicated)
        {

            int next = 1;
            foreach (var d in group.OrderBy(d => d.TimestampUtc).ThenBy(d => d.DecisionId))
            {
                if (d.Version != next)
                {
                    d.Version = next;
                    changed = true;
                }
                next++;
            }
        }
        if (changed) SaveChanges();
    }

    /// <summary>
    /// Antes de uma migração estrutural, copia o arquivo do banco para "&lt;db&gt;.bak-&lt;utc&gt;" (sem sobrescrever).
    /// Bancos em memória não têm arquivo e são ignorados.
    /// </summary>
    private static void BackupDatabaseFile(System.Data.Common.DbConnection connection)
    {
        string dataSource = connection.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:" || !File.Exists(dataSource)) return;

        string backupPath = BuildBackupPath(dataSource, DateTime.UtcNow, File.Exists);
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "VACUUM INTO $path;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$path";
        p.Value = backupPath;
        cmd.Parameters.Add(p);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Caminho do backup: "&lt;db&gt;.bak-yyyyMMddHHmmssfff" (UTC, com milissegundos). Se o nome já existir,
    /// acrescenta "-2", "-3"... porque VACUUM INTO não sobrescreve arquivo existente.
    /// </summary>
    public static string BuildBackupPath(string databasePath, DateTime utcNow, Func<string, bool> exists)
    {
        const int maxAttempts = 1000;
        string stamp = utcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        string candidate = $"{databasePath}.bak-{stamp}";
        for (int n = 2; exists(candidate); n++)
        {
            if (n > maxAttempts)
            {
                throw new IOException("Não foi possível escolher um nome livre para o backup do banco.");
            }
            candidate = $"{databasePath}.bak-{stamp}-{n}";
        }
        return candidate;
    }

    /// <summary>
    /// Troca a PK legada TranscriptWords(Seq) por (TranscriptId, Seq) preservando as linhas.
    /// Só atua se a tabela tem PK exatamente em Seq e a coluna TranscriptId; nunca descarta dados
    /// (a tabela antiga é renomeada, copiada e removida na mesma transação).
    /// Palavras sem TranscriptId (NULL) ou com TranscriptId sem Transcript correspondente não entram na tabela nova:
    /// vão para <see cref="TranscriptWordOrphansTable"/> (mesmas colunas, TranscriptId anulável), na mesma transação.
    /// </summary>
    private static void MigrateLegacyTranscriptWords(System.Data.Common.DbConnection connection, ILogger? logger)
    {
        var columns = ReadTableColumns(connection, "TranscriptWords");
        if (columns.Count == 0) return;

        var primaryKey = columns.Where(c => c.Pk > 0).OrderBy(c => c.Pk).Select(c => c.Name).ToList();
        bool isLegacy = primaryKey.Count == 1 && string.Equals(primaryKey[0], "Seq", StringComparison.OrdinalIgnoreCase);
        bool hasForeignKey = columns.Any(c => string.Equals(c.Name, TranscriptWordForeignKey, StringComparison.OrdinalIgnoreCase));
        if (!isLegacy || !hasForeignKey) return;

        // Sondagem antes da transação: a leitura de metadados não entra no tx da migração.
        bool hasTranscripts = ReadTableColumns(connection, "Transcripts").Count > 0;

        BackupDatabaseFile(connection);

        using var tx = connection.BeginTransaction();
        string legacy = "TranscriptWords" + LegacyTranscriptWordsSuffix;
        ExecuteNonQuery(connection, tx, $"ALTER TABLE TranscriptWords RENAME TO {legacy};");
        ExecuteNonQuery(connection, tx,
            $"CREATE TABLE TranscriptWords (" +
            $"{TranscriptWordForeignKey} TEXT NOT NULL, " +
            "Seq INTEGER NOT NULL, " +
            "StartSeconds REAL NOT NULL, " +
            "EndSeconds REAL NOT NULL, " +
            "Text TEXT NULL, " +
            "Confidence REAL NOT NULL, " +
            $"PRIMARY KEY ({TranscriptWordForeignKey}, Seq));");

        // Palavra válida: TranscriptId preenchido e apontando para um Transcript existente, e tempos/confiança não nulos
        // (a tabela nova exige NOT NULL). Demais linhas vão para a tabela de órfãs, nunca abortam a migração.
        // Sem a tabela Transcripts (legado muito antigo), só o NULL é tratado como órfão.
        string validCondition = $"{TranscriptWordForeignKey} IS NOT NULL AND StartSeconds IS NOT NULL AND EndSeconds IS NOT NULL AND Confidence IS NOT NULL" +
            (hasTranscripts ? $" AND {TranscriptWordForeignKey} IN (SELECT TranscriptId FROM Transcripts)" : string.Empty);
        string columnList = $"{TranscriptWordForeignKey}, Seq, StartSeconds, EndSeconds, Text, Confidence";

        ExecuteNonQuery(connection, tx,
            $"CREATE TABLE IF NOT EXISTS {TranscriptWordOrphansTable} (" +
            $"{TranscriptWordForeignKey} TEXT NULL, " +
            "Seq INTEGER NOT NULL, " +
            "StartSeconds REAL NULL, " +
            "EndSeconds REAL NULL, " +
            "Text TEXT NULL, " +
            "Confidence REAL NULL);");

        long orphanCount = ExecuteScalarLong(connection, tx, $"SELECT COUNT(*) FROM {legacy} WHERE NOT ({validCondition});");
        if (orphanCount > 0)
        {
            ExecuteNonQuery(connection, tx,
                $"INSERT INTO {TranscriptWordOrphansTable} ({columnList}) " +
                $"SELECT {columnList} FROM {legacy} WHERE NOT ({validCondition});");
        }

        ExecuteNonQuery(connection, tx,
            $"INSERT INTO TranscriptWords ({columnList}) " +
            $"SELECT {columnList} FROM {legacy} WHERE {validCondition};");
        ExecuteNonQuery(connection, tx, $"DROP TABLE {legacy};");
        tx.Commit();

        if (orphanCount > 0)
        {
            logger?.LogWarning(
                "Migração de TranscriptWords: {Count} palavra(s) sem Transcript válido preservada(s) em {Table}.",
                orphanCount, TranscriptWordOrphansTable);
        }
    }

    private static long ExecuteScalarLong(System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction? tx, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static List<(string Name, int Pk)> ReadTableColumns(System.Data.Common.DbConnection connection, string table)
    {
        var result = new List<(string Name, int Pk)>();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add((reader["name"].ToString()!, Convert.ToInt32(reader["pk"])));
        }
        return result;
    }

    private static void ExecuteNonQuery(System.Data.Common.DbConnection connection, System.Data.Common.DbTransaction? tx, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Adiciona colunas ausentes em uma tabela existente (idempotente). Tabela inexistente é ignorada.</summary>
    private static void EnsureColumns(System.Data.Common.DbConnection connection, string table, (string Name, string Definition)[] columns)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                existingColumns.Add(reader["name"].ToString()!);
            }
        }

        if (existingColumns.Count == 0)
        {
            return;
        }

        foreach (var (name, definition) in columns)
        {
            if (existingColumns.Contains(name))
            {
                continue;
            }

            using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {name} {definition};";
            alterCmd.ExecuteNonQuery();
        }
    }

    private static string SerializeTags(List<string>? tags)
        => JsonSerializer.Serialize(tags ?? new List<string>());

    private static List<string> DeserializeTags(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }
}
