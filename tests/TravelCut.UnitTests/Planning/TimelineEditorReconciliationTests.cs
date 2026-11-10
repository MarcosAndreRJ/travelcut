using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Planning;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Planning;
using TravelCut.Infrastructure.Repositories;
using Xunit;

namespace TravelCut.UnitTests.Planning;

public class TimelineEditorReconciliationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TravelCutDbContext _db;
    private readonly string _root;
    private readonly SqliteProjectRepository _projects;
    private readonly SqliteMediaRepository _media;
    private readonly SqliteEditPlanRepository _suggestions;
    private readonly SqlitePlanRevisionRepository _revisions;
    private readonly PlanArtifactStore _store = new();
    private readonly FakePipelineAnalysis _analysis = new();
    private readonly PlanningUseCases _useCases;

    public TimelineEditorReconciliationTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _root = Path.Combine(Path.GetTempPath(), "tc_f142_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _projects = new SqliteProjectRepository(_db);
        _media = new SqliteMediaRepository(_db);
        _suggestions = new SqliteEditPlanRepository(_db);
        _revisions = new SqlitePlanRevisionRepository(_db);

        _useCases = new PlanningUseCases(
            _projects,
            _media,
            _suggestions,
            _analysis,
            new ChronologyService(),
            _revisions,
            _store,
            new FileSystemFileExistence(),
            TimeProvider.System,
            NullLogger<PlanningUseCases>.Instance);
    }

    public void Dispose()
    {
        _connection.Dispose();
        _db.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    private static EditorialProfileDto CreateProfile() => new(
        ProfileId: "viagens",
        Version: 1,
        SilencePolicy: "balanced",
        Pace: "normal",
        JokePreservation: "strict",
        ZoomStrength: "subtle",
        SpeedBroll: "auto",
        MusicPolicy: "suggest_only",
        ReviewAmbiguous: true,
        AutoApplyTrivialCuts: true);

    private static TimelineTimeBaseDto CreateTimeBase() => new(
        FrameDurationNum: 1001,
        FrameDurationDen: 30000,
        Label: "29,97 fps");

    private static readonly Guid SharedMediaId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static EditPlanDto CreateTestPlan(Guid projectId, int revision)
    {
        var planId = Guid.NewGuid();
        var mediaId = SharedMediaId;
        var clipV1Id = Guid.NewGuid();
        var clipA1Id = Guid.NewGuid();

        var clipV1 = new PlanClipDto(
            ClipId: clipV1Id,
            TrackId: "V1",
            MediaId: mediaId,
            RelativePath: "cam1.mp4",
            SectionId: null,
            SourceInUs: 1_000_000,
            SourceOutUs: 4_000_000,
            TimelineStartFrame: 0,
            DurationFrames: 90,
            SpeedNum: 1,
            SpeedDen: 1,
            LinkedClipId: clipA1Id,
            Zoom: null,
            Rationale: "corte base",
            Evidence: new List<string>(),
            SuggestionIds: new List<Guid>(),
            ConfidenceBand: "high",
            NeedsHumanReview: false);

        var clipA1 = new PlanClipDto(
            ClipId: clipA1Id,
            TrackId: "A1",
            MediaId: mediaId,
            RelativePath: "cam1.mp4",
            SectionId: null,
            SourceInUs: 1_000_000,
            SourceOutUs: 4_000_000,
            TimelineStartFrame: 0,
            DurationFrames: 90,
            SpeedNum: 1,
            SpeedDen: 1,
            LinkedClipId: clipV1Id,
            Zoom: null,
            Rationale: "corte base",
            Evidence: new List<string>(),
            SuggestionIds: new List<Guid>(),
            ConfidenceBand: "high",
            NeedsHumanReview: false);

        return new EditPlanDto(
            SchemaVersion: "editplan/1",
            PlanId: planId,
            ProjectId: projectId,
            Revision: revision,
            BaseRevision: revision > 1 ? revision - 1 : null,
            CreatedAtUtc: DateTime.UtcNow,
            Author: "planner-rules/1",
            Profile: CreateProfile(),
            TimeBase: CreateTimeBase(),
            TimeZoneId: "America/Sao_Paulo",
            MediaSetFingerprint: "media_fp",
            PlanFingerprint: "plan_fp",
            Tracks: new List<PlanTrackDto>
            {
                new("V1", "video", "Video 1"),
                new("A1", "dialogue", "Audio 1")
            },
            Sections: new List<PlanSectionDto>(),
            Clips: new List<PlanClipDto> { clipV1, clipA1 },
            Transitions: new List<PlanTransitionDto>(),
            AppliedCuts: new List<PlanAppliedCutDto>(),
            Pending: new List<PlanPendingItemDto>(),
            Summary: new PlanSummaryDto(
                SourceDurationUs: 10_000_000,
                TimelineDurationFrames: 90,
                RemovedSourceUs: 7_000_000,
                ApprovedCutsApplied: 1,
                AutoCutsApplied: 0,
                PendingSuggestions: 0,
                ExcludedMedia: 0),
            Validation: new PlanValidationDto(true, new List<PlanIssueDto>(), DateTime.UtcNow));
    }

    [Fact]
    public void Reconcile_PreservesManualEdits_WhenCandidateAutoHasCompatibleMedia()
    {
        var projectId = Guid.NewGuid();
        var autoPlan = CreateTestPlan(projectId, 1);
        var manualPlan = CreateTestPlan(projectId, 2);

        var v1 = manualPlan.Clips.First(c => c.TrackId == "V1");
        var a1 = manualPlan.Clips.First(c => c.TrackId == "A1");

        var modifiedV1 = v1 with
        {
            SourceInUs = 1_500_000,
            SourceOutUs = 3_500_000,
            TimelineStartFrame = 30,
            DurationFrames = 60
        };
        var modifiedA1 = a1 with
        {
            SourceInUs = 1_500_000,
            SourceOutUs = 3_500_000,
            TimelineStartFrame = 30,
            DurationFrames = 60
        };

        var editedManualPlan = manualPlan with
        {
            Author = "user",
            Clips = new List<PlanClipDto> { modifiedV1, modifiedA1 }
        };

        var reconciled = PlanReconciler.Reconcile(autoPlan, editedManualPlan);

        Assert.False(reconciled.HasConflicts);
        Assert.Empty(reconciled.Conflicts);
        var reconciledV1 = reconciled.ReconciledPlan.Clips.First(c => c.TrackId == "V1");
        var reconciledA1 = reconciled.ReconciledPlan.Clips.First(c => c.TrackId == "A1");
        Assert.Equal(30, reconciledV1.TimelineStartFrame);
        Assert.Equal(60, reconciledV1.DurationFrames);
        Assert.Equal(1_500_000, reconciledV1.SourceInUs);
        Assert.Equal(30, reconciledA1.TimelineStartFrame);
        Assert.Equal(60, reconciledA1.DurationFrames);
    }

    [Fact]
    public void Reconcile_DetectsConflict_WhenManualClipMediaWasRemovedInAutoPlan()
    {
        var projectId = Guid.NewGuid();
        var autoPlan = CreateTestPlan(projectId, 1);
        var manualPlan = CreateTestPlan(projectId, 2);

        var autoWithoutMedia = autoPlan with
        {
            Clips = new List<PlanClipDto>()
        };

        var reconciled = PlanReconciler.Reconcile(autoWithoutMedia, manualPlan);

        Assert.True(reconciled.HasConflicts);
        Assert.NotEmpty(reconciled.Conflicts);
        Assert.Equal("MEDIA_REMOVED", reconciled.Conflicts[0].Type);
    }

    [Fact]
    public void Reconcile_DetectsConflict_WhenUnderlyingSourceIntervalWasAltered()
    {
        var projectId = Guid.NewGuid();
        var autoPlan = CreateTestPlan(projectId, 1);
        var manualPlan = CreateTestPlan(projectId, 2);

        var autoV1 = autoPlan.Clips.First(c => c.TrackId == "V1");
        var autoA1 = autoPlan.Clips.First(c => c.TrackId == "A1");
        var alteredAutoV1 = autoV1 with { SourceInUs = 8_000_000, SourceOutUs = 9_000_000 };
        var alteredAutoA1 = autoA1 with { SourceInUs = 8_000_000, SourceOutUs = 9_000_000 };

        var alteredAutoPlan = autoPlan with
        {
            Clips = new List<PlanClipDto> { alteredAutoV1, alteredAutoA1 }
        };

        var reconciled = PlanReconciler.Reconcile(alteredAutoPlan, manualPlan);

        Assert.True(reconciled.HasConflicts);
        Assert.Contains(reconciled.Conflicts, c => c.Type == "CUT_REOPENED" || c.Type == "INTERVAL_CONFLICT");
    }

    [Fact]
    public async Task SaveManualRevision_RejectsDesynchronizedV1A1_LinkMismatch()
    {
        var projectId = Guid.NewGuid();
        await _projects.SaveAsync(new Project { ProjectId = projectId, Name = "Teste", RootPath = _root });
        var plan = CreateTestPlan(projectId, 1);

        var v1 = plan.Clips.First(c => c.TrackId == "V1");
        var a1 = plan.Clips.First(c => c.TrackId == "A1");

        // Dessincronizar V1 e A1 intencionalmente
        var desyncedV1 = v1 with { TimelineStartFrame = 10 };
        var invalidPlan = plan with { Clips = new List<PlanClipDto> { desyncedV1, a1 } };

        var req = new SavePlanRevisionRequest(projectId, 1, invalidPlan);
        var result = await _useCases.SaveManualRevisionAsync(req);

        Assert.False(result.Success);
        Assert.Equal("LINK_MISMATCH", result.ErrorCode);
    }

    [Fact]
    public async Task SaveManualRevision_RejectsInvalidFrameInterval()
    {
        var projectId = Guid.NewGuid();
        await _projects.SaveAsync(new Project { ProjectId = projectId, Name = "Teste", RootPath = _root });
        var plan = CreateTestPlan(projectId, 1);

        var v1 = plan.Clips.First(c => c.TrackId == "V1");
        var a1 = plan.Clips.First(c => c.TrackId == "A1");

        // Duracao invalida <= 0
        var invalidV1 = v1 with { DurationFrames = 0 };
        var invalidA1 = a1 with { DurationFrames = 0 };
        var invalidPlan = plan with { Clips = new List<PlanClipDto> { invalidV1, invalidA1 } };

        var req = new SavePlanRevisionRequest(projectId, 1, invalidPlan);
        var result = await _useCases.SaveManualRevisionAsync(req);

        Assert.False(result.Success);
        Assert.Equal("INVALID_FRAME_INTERVAL", result.ErrorCode);
    }

    // F14.7a/E11: o save manual parte de uma revisao vigente do projeto (antes GetLatestAsync recebia o PlanId e
    // sempre devolvia null, entao o "salvar" era uma revisao 1 solta). Agora o plano base precisa existir: a revisao
    // gravada e a seguinte a vigente (2), com baseRevision = vigente.
    [Fact]
    public async Task SaveManualRevision_SucceedsAndBumpsRevision()
    {
        var projectId = Guid.NewGuid();
        await _projects.SaveAsync(new Project { ProjectId = projectId, Name = "Teste", RootPath = _root });
        var seeded = await SeedLatestAsync(projectId);

        var req = new SavePlanRevisionRequest(projectId, 1, seeded, Author: "user");
        var result = await _useCases.SaveManualRevisionAsync(req);

        Assert.True(result.Success, result.ErrorCode);
        Assert.NotNull(result.Value);
        Assert.Equal(2, result.Value.Revision);
        Assert.Equal(1, result.Value.BaseRevision);
        Assert.Equal("user", result.Value.Author);
        Assert.Equal(seeded.PlanId, result.Value.PlanId);
    }

    private async Task<EditPlanDto> SeedLatestAsync(Guid projectId, int revision = 1, string author = "planner-rules/1", string? root = null)
    {
        var plan = CreateTestPlan(projectId, revision) with { Author = author };
        plan = plan with { PlanFingerprint = PlanFingerprint.ComputePlan(PlanMapping.ToDomain(plan)) };
        await _store.WriteRevisionAsync(root ?? _root, plan.PlanId, plan.Revision, PlanJson.Serialize(plan));
        await _revisions.AddAsync(PlanMapping.ToIndexEntry(plan));
        return plan;
    }

    /// <summary>Hash de todos os arquivos sob a raiz + linhas do indice dos projetos informados.</summary>
    private async Task<string> SnapshotAsync(params Guid[] projectIds)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.Append(Path.GetRelativePath(_root, f)).Append('=')
              .Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(f)))).Append('\n');
        }
        foreach (var id in projectIds)
        {
            foreach (var e in await _revisions.ListAsync(id))
                sb.Append(id).Append('|').Append(e.Revision).Append('|').Append(e.PlanFingerprint).Append('|').Append(e.InputsFingerprint).Append('|').Append(e.Author).Append('\n');
        }
        return sb.ToString();
    }

    [Fact]
    public async Task SaveManualRevision_StoresRealInputsFingerprint_AndPinsBasisProfile()
    {
        var projectId = Guid.NewGuid();
        await _projects.SaveAsync(new Project { ProjectId = projectId, Name = "Teste", RootPath = _root });
        var seeded = await SeedLatestAsync(projectId);

        // O cliente tenta trocar perfil/base de tempo/fuso e se declarar autor automatico: nada disso vale.
        var tampered = seeded with
        {
            Profile = seeded.Profile with { SilencePolicy = "aggressive" },
            TimeBase = new TimelineTimeBaseDto(1, 24, "24 fps"),
            TimeZoneId = "UTC",
            Author = "planner-rules/1"
        };
        var result = await _useCases.SaveManualRevisionAsync(
            new SavePlanRevisionRequest(projectId, 1, tampered, Author: "planner-rules/1"));

        Assert.True(result.Success, result.ErrorCode);
        Assert.Equal("user", result.Value!.Author);
        Assert.Equal(seeded.Profile, result.Value.Profile);
        Assert.Equal(seeded.TimeBase, result.Value.TimeBase);
        Assert.Equal(seeded.TimeZoneId, result.Value.TimeZoneId);

        var entry = await _revisions.GetLatestAsync(projectId);
        Assert.Equal(2, entry!.Revision);
        Assert.False(string.IsNullOrWhiteSpace(entry.InputsFingerprint));
        Assert.Equal(projectId, entry.ProjectId);

        // O JSON gravado so tem validacao estrutural e fingerprint conferido.
        var json = await _store.ReadRevisionAsync(_root, entry.PlanId, entry.Revision);
        Assert.True(PlanJson.TryDeserialize(json!, out var stored));
        Assert.Equal(PlanFingerprint.ComputePlan(PlanMapping.ToDomain(stored!)), stored!.PlanFingerprint);
        Assert.DoesNotContain(stored.Validation.Issues, i => i.Code is "SOURCE_FILE_MISSING" or "PLAN_OUTDATED" or "MEDIA_CHANGED" or "PLAN_TAMPERED");
    }

    [Fact]
    public async Task SaveManualRevision_StaleBase_IsRejectedWithoutWriting()
    {
        var projectId = Guid.NewGuid();
        await _projects.SaveAsync(new Project { ProjectId = projectId, Name = "Teste", RootPath = _root });
        var seeded = await SeedLatestAsync(projectId);
        var first = await _useCases.SaveManualRevisionAsync(new SavePlanRevisionRequest(projectId, 1, seeded, Author: "user"));
        Assert.True(first.Success, first.ErrorCode);

        var before = await SnapshotAsync(projectId);
        // Segundo save ainda com baseRevision 1, mas a vigente ja e a 2.
        var stale = await _useCases.SaveManualRevisionAsync(new SavePlanRevisionRequest(projectId, 1, seeded, Author: "user"));

        Assert.False(stale.Success);
        Assert.Equal("PLAN_STALE_BASE", stale.ErrorCode);
        Assert.Equal(before, await SnapshotAsync(projectId));
    }

    [Fact]
    public async Task SaveManualRevision_WithoutBasePlan_IsRejected()
    {
        var projectId = Guid.NewGuid();
        await _projects.SaveAsync(new Project { ProjectId = projectId, Name = "Teste", RootPath = _root });

        var result = await _useCases.SaveManualRevisionAsync(new SavePlanRevisionRequest(projectId, 1, CreateTestPlan(projectId, 1)));

        Assert.False(result.Success);
        Assert.Equal("PLAN_NOT_FOUND", result.ErrorCode);
        Assert.Empty(await _revisions.ListAsync(projectId));
    }

    // R8: projeto cruzado. Nada pode ser gravado em outro projeto nem no proprio quando a identidade nao confere.
    [Fact]
    public async Task SaveManualRevision_CrossProject_WritesNothingAnywhere()
    {
        var projectA = Guid.NewGuid();
        var projectB = Guid.NewGuid();
        await _projects.SaveAsync(new Project { ProjectId = projectA, Name = "A", RootPath = _root });
        await _projects.SaveAsync(new Project { ProjectId = projectB, Name = "B", RootPath = Path.Combine(_root, "b") });
        var planA = await SeedLatestAsync(projectA);
        var planB = await SeedLatestAsync(projectB, root: Path.Combine(_root, "b"));
        var before = await SnapshotAsync(projectA, projectB);

        // 1) Plano do projeto B enviado na requisicao do projeto A.
        var wrongProject = await _useCases.SaveManualRevisionAsync(new SavePlanRevisionRequest(projectA, 1, planB, Author: "user"));
        Assert.False(wrongProject.Success);
        Assert.Equal("PLAN_PROJECT_MISMATCH", wrongProject.ErrorCode);

        // 2) ProjectId do cliente "corrigido" para A, mas com o PlanId do plano de B.
        var wrongPlanId = await _useCases.SaveManualRevisionAsync(
            new SavePlanRevisionRequest(projectA, 1, planB with { ProjectId = projectA }, Author: "user"));
        Assert.False(wrongPlanId.Success);
        Assert.Equal("PLAN_ID_MISMATCH", wrongPlanId.ErrorCode);

        // 3) Projeto B recebe plano de A.
        var reverse = await _useCases.SaveManualRevisionAsync(
            new SavePlanRevisionRequest(projectB, 1, planA with { ProjectId = projectB }, Author: "user"));
        Assert.False(reverse.Success);
        Assert.Equal("PLAN_ID_MISMATCH", reverse.ErrorCode);

        Assert.Equal(before, await SnapshotAsync(projectA, projectB));
    }
}
