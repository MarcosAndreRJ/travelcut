using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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

/// <summary>Use cases do plano com SQLite em memória (EF real) e armazenamento de arquivos real em pasta temporária.</summary>
public class PlanningUseCasesTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TravelCutDbContext _db;
    private readonly string _root;
    private readonly SqliteMediaRepository _media;
    private readonly SqliteEditPlanRepository _suggestions;
    private readonly SqliteProjectRepository _projects;
    private readonly SqlitePlanRevisionRepository _revisions;
    private readonly PlanArtifactStore _store = new();
    private readonly FakePipelineAnalysis _analysis = new();
    private readonly PlanningUseCases _useCases;

    public PlanningUseCasesTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _db = new TravelCutDbContext(new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        _root = Path.Combine(Path.GetTempPath(), "tc_plan_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _projects = new SqliteProjectRepository(_db);
        _media = new SqliteMediaRepository(_db);
        _suggestions = new SqliteEditPlanRepository(_db);
        _revisions = new SqlitePlanRevisionRepository(_db);

        _projects.SaveAsync(new Project { ProjectId = TravelFixture.ProjectId, Name = "Viagem", RootPath = _root }).GetAwaiter().GetResult();
        _media.SaveBatchAsync(TravelFixture.Media()).GetAwaiter().GetResult();
        // Arquivos reais das mídias da viagem: a validação SOURCE_FILE_MISSING só acusa o que sumiu do disco.
        foreach (var m in TravelFixture.Media())
        {
            var full = Path.Combine(_root, m.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "midia-de-teste");
        }
        foreach (var s in TravelFixture.Suggestions()) _suggestions.SaveSuggestionsBatchAsync([s]).GetAwaiter().GetResult();
        foreach (var d in TravelFixture.Decisions()) _suggestions.SaveDecisionAsync(d).GetAwaiter().GetResult();
        foreach (var (id, analysis) in TravelFixture.Analyses()) _analysis.Analyses[id] = analysis;

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

    /// <summary>Armazenamento que delega a leitura e recusa toda gravação como "número ocupado" (conflito).</summary>
    private sealed class AlwaysConflictStore(IPlanArtifactStore inner) : IPlanArtifactStore
    {
        public Task WriteRevisionAsync(string projectRoot, Guid planId, int revision, string json, CancellationToken ct = default) =>
            throw new PlanRevisionConflictException("ocupado");
        public Task<string?> ReadRevisionAsync(string projectRoot, Guid planId, int revision, CancellationToken ct = default) =>
            inner.ReadRevisionAsync(projectRoot, planId, revision, ct);
        public Task<IReadOnlyList<int>> ListRevisionNumbersAsync(string projectRoot, Guid planId, CancellationToken ct = default) =>
            inner.ListRevisionNumbersAsync(projectRoot, planId, ct);
        public Task DeleteUncommittedRevisionAsync(string projectRoot, Guid planId, int revision, CancellationToken ct = default) =>
            inner.DeleteUncommittedRevisionAsync(projectRoot, planId, revision, ct);
    }

    private PlanningUseCases UseCasesWith(IPlanArtifactStore store) => new(
        _projects, _media, _suggestions, _analysis, new ChronologyService(), _revisions, store,
        new FileSystemFileExistence(), TimeProvider.System, NullLogger<PlanningUseCases>.Instance);

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Pasta temporária: limpeza melhor esforço.
        }
    }

    private string RevisionFile(Guid planId, int revision) =>
        Path.Combine(_root, "3-Trabalho", "plans", planId.ToString("D"), $"r{revision:D4}.json");

    private async Task<EditPlanDto> GenerateAsync(EditorialProfileDto? profile = null)
    {
        var result = await _useCases.GenerateAsync(new GeneratePlanRequest(TravelFixture.ProjectId, profile, null));
        Assert.True(result.Success, result.ErrorMessage);
        return result.Value!;
    }

    private static readonly string[] InputChanges = ["approve", "reject", "defer"];
    private int _inputChangeIndex;

    /// <summary>
    /// Muda uma entrada real do plano (decisão na sugestão 4, sem regenerar). Ações consecutivas produzem conteúdos
    /// diferentes (aplicado / removido / pendente), então a próxima geração cria revisão nova.
    /// </summary>
    private async Task ChangeInputAsync()
    {
        var action = InputChanges[_inputChangeIndex++ % InputChanges.Length];
        await _suggestions.SaveDecisionAsync(new EditDecision
        {
            SuggestionId = TravelFixture.S(4),
            Action = action,
            Reviewer = "user",
            TimestampUtc = DateTime.UtcNow,
        });
    }

    private async Task<EditPlanDto> GenerateChangedAsync()
    {
        await ChangeInputAsync();
        return await GenerateAsync();
    }

    [Fact]
    public async Task Generate_FirstRevision_WritesArtifactAndIndex()
    {
        var plan = await GenerateAsync();

        Assert.Equal(1, plan.Revision);
        Assert.Null(plan.BaseRevision);
        Assert.Equal("planner-rules/1", plan.Author);
        Assert.True(File.Exists(RevisionFile(plan.PlanId, 1)));

        var list = await _useCases.ListAsync(TravelFixture.ProjectId);
        var info = Assert.Single(list.Value!.Revisions);
        Assert.Equal(plan.PlanFingerprint, info.PlanFingerprint);
        Assert.Equal(plan.Clips.Count, info.ClipCount);
        Assert.True(info.IsValid);
    }

    [Fact]
    public async Task Generate_TwiceWithSameInputs_IsIdempotent_NoNewRevision_AndNeverOverwrites()
    {
        // plan.generate é idempotente: conteúdo igual ao da revisão mais recente não cria revisão nova.
        var first = await GenerateAsync();
        var file1 = RevisionFile(first.PlanId, 1);
        var bytesBefore = await File.ReadAllBytesAsync(file1);

        var second = await GenerateAsync();

        Assert.Equal(1, second.Revision);
        Assert.Equal(first.PlanId, second.PlanId);
        Assert.Equal(first.PlanFingerprint, second.PlanFingerprint);
        Assert.Equal(bytesBefore, await File.ReadAllBytesAsync(file1));
        Assert.False(File.Exists(RevisionFile(first.PlanId, 2)));
        Assert.Single((await _useCases.ListAsync(TravelFixture.ProjectId)).Value!.Revisions);
    }

    [Fact]
    public async Task Generate_AfterInputChange_CreatesNewRevision_WithBaseRevision()
    {
        var first = await GenerateAsync();

        var second = await GenerateChangedAsync();

        Assert.Equal(2, second.Revision);
        Assert.Equal(1, second.BaseRevision);
        Assert.Equal(first.PlanId, second.PlanId);
        Assert.NotEqual(first.PlanFingerprint, second.PlanFingerprint);
        Assert.True(File.Exists(RevisionFile(second.PlanId, 2)));
    }

    [Fact]
    public async Task Revert_CreatesNewRevision_WithSameContent_BasedOnTarget()
    {
        var first = await GenerateAsync();
        await GenerateChangedAsync();

        var reverted = (await _useCases.RevertAsync(new RevertPlanRequest(TravelFixture.ProjectId, 1))).Value!;

        Assert.Equal(3, reverted.Revision);
        Assert.Equal(1, reverted.BaseRevision);
        Assert.Equal("revert", reverted.Author);
        Assert.Equal(first.PlanFingerprint, reverted.PlanFingerprint);
        Assert.Equal(JsonSerializer.Serialize(first.Clips), JsonSerializer.Serialize(reverted.Clips));
        Assert.Equal(JsonSerializer.Serialize(first.Sections), JsonSerializer.Serialize(reverted.Sections));
        Assert.Equal(3, (await _useCases.ListAsync(TravelFixture.ProjectId)).Value!.Revisions.Count);
        Assert.True(File.Exists(RevisionFile(first.PlanId, 3)));
    }

    [Fact]
    public async Task Get_WithoutPlan_ReturnsNullSuccess()
    {
        var result = await _useCases.GetAsync(new GetPlanRequest(TravelFixture.ProjectId, null));

        Assert.True(result.Success);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Get_LatestAndSpecificRevision_ReadFromArtifacts()
    {
        var first = await GenerateAsync();
        var second = await GenerateChangedAsync();

        var latest = (await _useCases.GetAsync(new GetPlanRequest(TravelFixture.ProjectId, null))).Value!;
        var one = (await _useCases.GetAsync(new GetPlanRequest(TravelFixture.ProjectId, 1))).Value!;

        Assert.Equal(second.Revision, latest.Revision);
        Assert.Equal(first.Revision, one.Revision);
        Assert.Equal(first.PlanFingerprint, one.PlanFingerprint);
    }

    [Fact]
    public async Task Get_UnknownRevision_IsPlanNotFound()
    {
        await GenerateAsync();

        var result = await _useCases.GetAsync(new GetPlanRequest(TravelFixture.ProjectId, 99));

        Assert.Equal("PLAN_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task MissingArtifact_IsReported_AndNeverRebuiltSilently()
    {
        var first = await GenerateAsync();
        await GenerateChangedAsync();
        File.Delete(RevisionFile(first.PlanId, 2));

        var get = await _useCases.GetAsync(new GetPlanRequest(TravelFixture.ProjectId, 2));
        var validate = await _useCases.ValidateAsync(new ValidatePlanRequest(TravelFixture.ProjectId, 2));

        Assert.Equal("PLAN_FILE_MISSING", get.ErrorCode);
        Assert.Equal("PLAN_FILE_MISSING", validate.ErrorCode);
        Assert.False(File.Exists(RevisionFile(first.PlanId, 2)));
    }

    [Fact]
    public async Task Validate_UnchangedMedia_HasNoMediaChangedAndSavesNothing()
    {
        await GenerateAsync();

        var validation = (await _useCases.ValidateAsync(new ValidatePlanRequest(TravelFixture.ProjectId, null))).Value!;

        Assert.DoesNotContain(validation.Issues, i => i.Code == "MEDIA_CHANGED");
        Assert.Single((await _useCases.ListAsync(TravelFixture.ProjectId)).Value!.Revisions);
    }

    [Fact]
    public async Task Validate_AfterMediaChange_ReportsMediaChanged_WithoutSaving()
    {
        await GenerateAsync();
        var changed = TravelFixture.Media().Single(m => m.MediaId == TravelFixture.M(2));
        changed.Fingerprint = "fp-2-alterado";
        await _media.SaveAsync(changed);

        var validation = (await _useCases.ValidateAsync(new ValidatePlanRequest(TravelFixture.ProjectId, 1))).Value!;

        Assert.Contains(validation.Issues, i => i.Code == "MEDIA_CHANGED");
        Assert.True(validation.IsValid); // aviso: o plano continua válido, só está desatualizado
        Assert.Single((await _useCases.ListAsync(TravelFixture.ProjectId)).Value!.Revisions);
    }

    [Fact]
    public async Task Generate_OnlyUnusableMedia_ReturnsNoMediaToPlan_AndWritesNothing()
    {
        var otherProject = Guid.NewGuid();
        await _projects.SaveAsync(new Project { ProjectId = otherProject, Name = "Sem mídia", RootPath = Path.Combine(_root, "outro") });
        var failed = TravelFixture.Media().Single(m => m.MediaId == TravelFixture.M(7));
        failed.MediaId = Guid.NewGuid();
        failed.Metadata!.MediaId = failed.MediaId;
        failed.ProjectId = otherProject;
        await _media.SaveAsync(failed);

        var result = await _useCases.GenerateAsync(new GeneratePlanRequest(otherProject, null, null));

        Assert.Equal("NO_MEDIA_TO_PLAN", result.ErrorCode);
        Assert.Empty((await _useCases.ListAsync(otherProject)).Value!.Revisions);
    }

    [Fact]
    public async Task Generate_UnknownProject_IsProjectNotFound()
    {
        var result = await _useCases.GenerateAsync(new GeneratePlanRequest(Guid.NewGuid(), null, null));

        Assert.Equal("PROJECT_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Generate_InvalidArguments_AreRejectedWithInvalidArgument()
    {
        var badProfile = EditorialProfiles.Default() with { ProfileId = "outro" };

        var profileResult = await _useCases.GenerateAsync(new GeneratePlanRequest(TravelFixture.ProjectId, badProfile, null));
        var timeBaseResult = await _useCases.GenerateAsync(new GeneratePlanRequest(TravelFixture.ProjectId, null, new TimelineTimeBaseDto(0, 30, "x")));
        var revertResult = await _useCases.RevertAsync(new RevertPlanRequest(TravelFixture.ProjectId, 0));

        Assert.Equal("INVALID_ARGUMENT", profileResult.ErrorCode);
        Assert.Equal("INVALID_ARGUMENT", timeBaseResult.ErrorCode);
        Assert.Equal("INVALID_ARGUMENT", revertResult.ErrorCode);
    }

    [Fact]
    public async Task Revert_UnknownRevision_IsPlanNotFound()
    {
        await GenerateAsync();

        var result = await _useCases.RevertAsync(new RevertPlanRequest(TravelFixture.ProjectId, 42));

        Assert.Equal("PLAN_NOT_FOUND", result.ErrorCode);
    }

    [Fact]
    public async Task Generate_RecordsReviewAmbiguousTrue_EvenWhenRequestedFalse()
    {
        var profile = EditorialProfiles.Default() with { ReviewAmbiguous = false };

        var plan = await GenerateAsync(profile);

        Assert.True(plan.Profile.ReviewAmbiguous);
    }

    [Fact]
    public async Task OrphanRevisionFile_WithoutIndex_DoesNotBlockGeneration_NextIsMaxPlusOne()
    {
        // A3: r0002.json sem linha de índice (gravação interrompida) → a próxima revisão é a 3.
        var first = await GenerateAsync();
        File.Copy(RevisionFile(first.PlanId, 1), RevisionFile(first.PlanId, 2));

        var next = await GenerateChangedAsync();

        Assert.Equal(3, next.Revision);
        Assert.Equal(1, next.BaseRevision);
        Assert.True(File.Exists(RevisionFile(first.PlanId, 3)));
        var listed = (await _useCases.ListAsync(TravelFixture.ProjectId)).Value!.Revisions.Select(r => r.Revision);
        Assert.Equal(new[] { 1, 3 }, listed);
    }

    [Fact]
    public async Task OrphanRevisionFile_AlsoBlocksNothing_OnRevert()
    {
        var first = await GenerateAsync();
        await GenerateChangedAsync();
        File.Copy(RevisionFile(first.PlanId, 2), RevisionFile(first.PlanId, 3)); // órfã 3 sem índice

        var reverted = (await _useCases.RevertAsync(new RevertPlanRequest(TravelFixture.ProjectId, 1))).Value!;

        Assert.Equal(4, reverted.Revision);
        Assert.Equal("revert", reverted.Author);
    }

    [Fact]
    public async Task RevisionNumberAlreadyTaken_IsPlanRevisionConflict_NotInternalError()
    {
        // A3: IOException de "já existe" vira PLAN_REVISION_CONFLICT; nada entra no índice.
        var conflicting = UseCasesWith(new AlwaysConflictStore(_store));

        var result = await conflicting.GenerateAsync(new GeneratePlanRequest(TravelFixture.ProjectId, null, null, "UTC"));

        Assert.False(result.Success);
        Assert.Equal("PLAN_REVISION_CONFLICT", result.ErrorCode);
        Assert.Empty((await _useCases.ListAsync(TravelFixture.ProjectId)).Value!.Revisions);
    }

    [Fact]
    public async Task Store_RefusesToOverwriteExistingRevision_WithConflictException()
    {
        var first = await GenerateAsync();
        var json = await File.ReadAllTextAsync(RevisionFile(first.PlanId, 1));

        await Assert.ThrowsAsync<PlanRevisionConflictException>(() =>
            _store.WriteRevisionAsync(_root, first.PlanId, 1, json));
        Assert.Equal(json, await File.ReadAllTextAsync(RevisionFile(first.PlanId, 1)));
    }

    [Fact]
    public async Task TamperedArtifact_IsReported_WithPlanTampered_InGetAndValidate_AndRevertIsRefused()
    {
        // M4: o plano volta com issue de validação error PLAN_TAMPERED (não escondido) e o revert é recusado.
        var first = await GenerateAsync();
        var file = RevisionFile(first.PlanId, 1);
        var dto = PlanJson.TryDeserialize(await File.ReadAllTextAsync(file), out var parsed) ? parsed! : throw new InvalidOperationException();
        var clip0 = dto.Clips[0] with { Rationale = "texto alterado fora do TravelCut" };
        var tampered = dto with { Clips = [clip0, .. dto.Clips.Skip(1)] };
        await File.WriteAllTextAsync(file, PlanJson.Serialize(tampered));

        var get = (await _useCases.GetAsync(new GetPlanRequest(TravelFixture.ProjectId, 1))).Value!;
        var validate = (await _useCases.ValidateAsync(new ValidatePlanRequest(TravelFixture.ProjectId, 1))).Value!;
        var revert = await _useCases.RevertAsync(new RevertPlanRequest(TravelFixture.ProjectId, 1));

        Assert.False(get.Validation.IsValid);
        Assert.Contains(get.Validation.Issues, i => i.Code == "PLAN_TAMPERED" && i.Severity == "error");
        Assert.Contains(validate.Issues, i => i.Code == "PLAN_TAMPERED" && i.Severity == "error");
        Assert.False(validate.IsValid);
        Assert.Equal("PLAN_TAMPERED", revert.ErrorCode);
        Assert.Single((await _useCases.ListAsync(TravelFixture.ProjectId)).Value!.Revisions);
    }

    [Fact]
    public async Task UntouchedArtifact_HasNoTamperIssue()
    {
        var first = await GenerateAsync();

        var get = (await _useCases.GetAsync(new GetPlanRequest(TravelFixture.ProjectId, 1))).Value!;

        Assert.DoesNotContain(get.Validation.Issues, i => i.Code == "PLAN_TAMPERED");
        Assert.Equal(first.PlanFingerprint, get.PlanFingerprint);
    }

    [Theory]
    [InlineData("{ isto não é json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task InvalidOrNullArtifact_IsPlanFileCorrupt_NotInternalError(string content)
    {
        // M9: JSON inválido, "null" ou estrutura incompleta → PLAN_FILE_CORRUPT (nunca plano vazio).
        var first = await GenerateAsync();
        await File.WriteAllTextAsync(RevisionFile(first.PlanId, 1), content);

        var get = await _useCases.GetAsync(new GetPlanRequest(TravelFixture.ProjectId, 1));
        var validate = await _useCases.ValidateAsync(new ValidatePlanRequest(TravelFixture.ProjectId, 1));
        var revert = await _useCases.RevertAsync(new RevertPlanRequest(TravelFixture.ProjectId, 1));

        Assert.Equal("PLAN_FILE_CORRUPT", get.ErrorCode);
        Assert.Null(get.Value);
        Assert.Equal("PLAN_FILE_CORRUPT", validate.ErrorCode);
        Assert.Equal("PLAN_FILE_CORRUPT", revert.ErrorCode);
    }

    [Fact]
    public async Task Generate_WithTimeZoneId_UsesItForTitles_AndRevertKeepsIt()
    {
        // M7: fuso informado é usado em títulos e fica no plano; revert preserva o fuso da revisão alvo.
        var plan = (await _useCases.GenerateAsync(new GeneratePlanRequest(
            TravelFixture.ProjectId, null, null, "E. South America Standard Time"))).Value!;

        Assert.Equal("E. South America Standard Time", plan.TimeZoneId);
        Assert.Equal("Sessão 1 — 14/10 06:00", plan.Sections[0].Title); // 09:00 UTC em UTC−3 (sem horário de verão em 2026)

        var reverted = (await _useCases.RevertAsync(new RevertPlanRequest(TravelFixture.ProjectId, plan.Revision))).Value!;
        Assert.Equal("E. South America Standard Time", reverted.TimeZoneId);
    }

    [Fact]
    public async Task Generate_UnknownTimeZoneId_IsInvalidArgument()
    {
        var result = await _useCases.GenerateAsync(new GeneratePlanRequest(
            TravelFixture.ProjectId, null, null, "Fuso/Inexistente"));

        Assert.Equal("INVALID_ARGUMENT", result.ErrorCode);
        Assert.Empty((await _useCases.ListAsync(TravelFixture.ProjectId)).Value!.Revisions);
    }

    [Fact]
    public void GetProfile_ReturnsViagensDefault()
    {
        var profile = _useCases.GetProfile();

        Assert.Equal(EditorialProfiles.Default(), profile);
    }
}
