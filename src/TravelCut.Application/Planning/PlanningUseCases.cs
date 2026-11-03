using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Domain.Planning;

namespace TravelCut.Application.Planning;

/// <summary>Resultado de um use case: sucesso (ErrorCode null) ou código de erro estável + mensagem PT-BR genérica.</summary>
public sealed record PlanOutcome<T>(T? Value, string? ErrorCode = null, string? ErrorMessage = null)
{
    public bool Success => ErrorCode is null;

    public static PlanOutcome<T> Ok(T? value) => new(value);

    public static PlanOutcome<T> Fail(string code, string message) => new(default, code, message);
}

/// <summary>Payload de plan.validate (revision null = mais recente).</summary>
public sealed record ValidatePlanRequest(Guid ProjectId, int? Revision);

/// <summary>
/// Use cases do plano editorial: perfil, geração, leitura, listagem, validação (sem gravar) e revert.
/// Revisões são imutáveis: cada geração/revert cria uma nova revisão (arquivo JSON novo + linha de índice).
/// Escritas por projeto são serializadas (um lock por projeto dentro do processo).
/// </summary>
public sealed partial class PlanningUseCases
{
    /// <summary>Revisão cujas entradas (sugestões/decisões/mídias/perfil) mudaram desde a geração: aviso, não bloqueia.</summary>
    public const string PlanOutdatedCode = "PLAN_OUTDATED";

    /// <summary>Arquivo de mídia usado por um clipe não existe mais em disco (RootPath + RelativePath): erro de validação.</summary>
    public const string SourceFileMissingCode = "SOURCE_FILE_MISSING";

    /// <summary>Mídia mudou desde a geração (fingerprint do conjunto de mídias): dependente de estado externo.</summary>
    public const string MediaChangedCode = "MEDIA_CHANGED";

    /// <summary>
    /// Issues que dependem de estado externo ao artefato (disco, mídias, decisões, fingerprint do índice). Nunca são
    /// gravadas no JSON da revisão: são recalculadas em cada leitura (plan.get, plan.validate, geração e revert).
    /// </summary>
    private static readonly HashSet<string> DerivedIssueCodes =
        [SourceFileMissingCode, PlanOutdatedCode, MediaChangedCode, TamperedCode];

    private const string PlanOutdatedMessage =
        "O plano foi gerado com entradas diferentes das atuais (decisões, mídias ou perfil mudaram). Gere um novo plano.";

    private const string SourceFileMissingMessage = "Arquivo da mídia não encontrado em disco: ";

    public const string AuthorRevert = "revert";
    public const string NoProjectMessage = "Projeto não encontrado.";

    /// <summary>Código de erro (fail) ou de issue de validação quando o conteúdo não confere com o fingerprint gravado.</summary>
    public const string TamperedCode = "PLAN_TAMPERED";

    private const string TamperedMessage =
        "O conteúdo desta revisão não confere com o fingerprint gravado: o arquivo pode ter sido alterado fora do TravelCut.";
    private const string MissingMessage = "O arquivo desta revisão do plano não foi encontrado. Não é reconstruído automaticamente.";
    private const string CorruptMessage = "O arquivo desta revisão do plano está inválido (JSON corrompido ou incompleto).";
    private const string ConflictMessage = "Outra gravação ocupou o número desta revisão. Tente novamente.";

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ProjectLocks = new();

    private readonly IProjectRepository _projects;
    private readonly IMediaRepository _media;
    private readonly IEditPlanRepository _suggestionsAndDecisions;
    private readonly IPipelineJobManager _analysis;
    private readonly IChronologyService _chronology;
    private readonly IPlanRevisionRepository _revisions;
    private readonly IPlanArtifactStore _artifacts;
    private readonly IFileExistence _files;
    private readonly TimeProvider _clock;
    private readonly ILogger<PlanningUseCases> _logger;

    public PlanningUseCases(
        IProjectRepository projects,
        IMediaRepository media,
        IEditPlanRepository suggestionsAndDecisions,
        IPipelineJobManager analysis,
        IChronologyService chronology,
        IPlanRevisionRepository revisions,
        IPlanArtifactStore artifacts,
        IFileExistence files,
        TimeProvider clock,
        ILogger<PlanningUseCases> logger)
    {
        _projects = projects;
        _media = media;
        _suggestionsAndDecisions = suggestionsAndDecisions;
        _analysis = analysis;
        _chronology = chronology;
        _revisions = revisions;
        _artifacts = artifacts;
        _files = files;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Resultado da leitura de um artefato: plano válido ou código de erro (arquivo ausente ou corrompido).</summary>
    private sealed record PlanRead(EditPlanDto? Plan, string? ErrorCode, string? ErrorMessage);

    public EditorialProfileDto GetProfile() => EditorialProfiles.Default();

    public async Task<PlanOutcome<EditPlanDto>> GenerateAsync(GeneratePlanRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<EditPlanDto>.Fail("INVALID_ARGUMENT", "ID de projeto inválido.");

        var profile = EditorialProfiles.Resolve(request.Profile, out var profileError);
        if (profileError is not null)
            return PlanOutcome<EditPlanDto>.Fail("INVALID_ARGUMENT", profileError);

        if (request.TimeBase is { } tb && (tb.FrameDurationNum <= 0 || tb.FrameDurationDen <= 0))
            return PlanOutcome<EditPlanDto>.Fail("INVALID_ARGUMENT", "Base de tempo inválida (numerador e denominador devem ser positivos).");

        if (!TryResolveZone(request.TimeZoneId, out var zone, out var zoneId, out var zoneError))
            return PlanOutcome<EditPlanDto>.Fail("INVALID_ARGUMENT", zoneError!);

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null || string.IsNullOrWhiteSpace(project.RootPath))
            return PlanOutcome<EditPlanDto>.Fail("PROJECT_NOT_FOUND", NoProjectMessage);

        return await WithProjectLockAsync(project.ProjectId, async () =>
        {
            var latest = await _revisions.GetLatestAsync(project.ProjectId, ct);
            var planId = latest?.PlanId ?? Guid.NewGuid();
            var revision = await NextRevisionAsync(project.RootPath, planId, latest?.Revision, ct);
            var input = await LoadInputAsync(project.ProjectId, profile, request.TimeBase, zone, zoneId, ct);
            var context = new PlanBuildContext(
                PlanId: planId,
                Revision: revision,
                BaseRevision: latest?.Revision,
                CreatedAtUtc: _clock.GetUtcNow().UtcDateTime,
                Author: EditPlanner.Author);

            var built = EditPlanner.Build(input, context);
            if (built.Plan is null)
                return PlanOutcome<EditPlanDto>.Fail(built.ErrorCode ?? "NO_MEDIA_TO_PLAN", built.ErrorMessage ?? EditPlanner.NoMediaMessage);

            // Idempotente: conteúdo (planFingerprint) igual ao da revisão mais recente → não cria revisão nova.
            if (latest is not null
                && string.Equals(latest.PlanFingerprint, built.Plan.PlanFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Plano {PlanId}: conteúdo igual à revisão {Revision}; nenhuma revisão nova criada",
                    latest.PlanId, latest.Revision);
                // A revisão vigente continua correta para as entradas atuais: atualiza só o metadado do índice,
                // para que uma decisão sem efeito no plano não deixe a revisão marcada como PLAN_OUTDATED.
                var currentInputs = InputsFingerprintOf(built.Plan, input.Media, input.Suggestions, input.Decisions);
                await _revisions.UpdateInputsFingerprintAsync(project.ProjectId, latest.Revision, currentInputs, ct);
                return await ReadDerivedAsync(project.ProjectId, project.RootPath, latest, currentInputs, ct);
            }

            // Persistido só o que é estrutural do plano; SOURCE_FILE_MISSING etc. são derivados e não entram no JSON.
            var plan = WithStructuralValidation(built.Plan);
            var inputsFingerprint = InputsFingerprintOf(plan, input.Media, input.Suggestions, input.Decisions);

            var conflict = await TryPersistAsync(project.RootPath, plan, inputsFingerprint, ct);
            if (conflict is not null) return conflict;

            _logger.LogInformation("Plano {PlanId} revisão {Revision} gerado com {ClipCount} clipe(s)",
                plan.PlanId, plan.Revision, plan.Clips.Count);
            var derived = await DeriveValidationAsync(project.ProjectId, project.RootPath, plan, inputsFingerprint, ct);
            return PlanOutcome<EditPlanDto>.Ok(plan with { Validation = derived });
        }, ct);
    }

    /// <summary>Sem plano no projeto retorna sucesso com valor null.</summary>
    public async Task<PlanOutcome<EditPlanDto?>> GetAsync(GetPlanRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<EditPlanDto?>.Fail("INVALID_ARGUMENT", "ID de projeto inválido.");
        if (request.Revision is < 1)
            return PlanOutcome<EditPlanDto?>.Fail("INVALID_ARGUMENT", "Número de revisão inválido.");

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null) return PlanOutcome<EditPlanDto?>.Fail("PROJECT_NOT_FOUND", NoProjectMessage);

        var entry = request.Revision is int revision
            ? await _revisions.GetAsync(request.ProjectId, revision, ct)
            : await _revisions.GetLatestAsync(request.ProjectId, ct);

        if (entry is null)
        {
            return request.Revision is null
                ? PlanOutcome<EditPlanDto?>.Ok(null)
                : PlanOutcome<EditPlanDto?>.Fail("PLAN_NOT_FOUND", "Revisão do plano não encontrada.");
        }

        // Validação derivada no momento (sem gravar): plano adulterado não é escondido, volta com PLAN_TAMPERED.
        var derived = await ReadDerivedAsync(project.ProjectId, project.RootPath, entry, entry.InputsFingerprint, ct);
        return derived.Success
            ? PlanOutcome<EditPlanDto?>.Ok(derived.Value)
            : PlanOutcome<EditPlanDto?>.Fail(derived.ErrorCode!, derived.ErrorMessage!);
    }

    public async Task<PlanOutcome<PlanListResponse>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        if (projectId == Guid.Empty)
            return PlanOutcome<PlanListResponse>.Fail("INVALID_ARGUMENT", "ID de projeto inválido.");
        var project = await _projects.GetByIdAsync(projectId, ct);
        if (project is null) return PlanOutcome<PlanListResponse>.Fail("PROJECT_NOT_FOUND", NoProjectMessage);

        var entries = await _revisions.ListAsync(projectId, ct);
        return PlanOutcome<PlanListResponse>.Ok(new PlanListResponse(entries.Select(PlanMapping.ToInfo).ToList()));
    }

    /// <summary>Revalida contra o estado ATUAL das mídias e confere o fingerprint. Não grava nada.</summary>
    public async Task<PlanOutcome<PlanValidationDto>> ValidateAsync(ValidatePlanRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<PlanValidationDto>.Fail("INVALID_ARGUMENT", "ID de projeto inválido.");
        if (request.Revision is < 1)
            return PlanOutcome<PlanValidationDto>.Fail("INVALID_ARGUMENT", "Número de revisão inválido.");

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null) return PlanOutcome<PlanValidationDto>.Fail("PROJECT_NOT_FOUND", NoProjectMessage);

        var entry = request.Revision is int revision
            ? await _revisions.GetAsync(request.ProjectId, revision, ct)
            : await _revisions.GetLatestAsync(request.ProjectId, ct);
        if (entry is null) return PlanOutcome<PlanValidationDto>.Fail("PLAN_NOT_FOUND", "Revisão do plano não encontrada.");

        var read = await ReadPlanAsync(project.RootPath, entry, ct);
        if (read.ErrorCode is not null)
            return PlanOutcome<PlanValidationDto>.Fail(read.ErrorCode, read.ErrorMessage!);

        var validation = await DeriveValidationAsync(project.ProjectId, project.RootPath, read.Plan!, entry.InputsFingerprint, ct);
        return PlanOutcome<PlanValidationDto>.Ok(validation);
    }

    /// <summary>Nova revisão com o conteúdo da revisão alvo; baseRevision = alvo; revalidada contra as mídias atuais.</summary>
    public async Task<PlanOutcome<EditPlanDto>> RevertAsync(RevertPlanRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<EditPlanDto>.Fail("INVALID_ARGUMENT", "ID de projeto inválido.");
        if (request.ToRevision < 1)
            return PlanOutcome<EditPlanDto>.Fail("INVALID_ARGUMENT", "Número de revisão inválido.");

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null) return PlanOutcome<EditPlanDto>.Fail("PROJECT_NOT_FOUND", NoProjectMessage);

        return await WithProjectLockAsync(project.ProjectId, async () =>
        {
            var target = await _revisions.GetAsync(project.ProjectId, request.ToRevision, ct);
            if (target is null) return PlanOutcome<EditPlanDto>.Fail("PLAN_NOT_FOUND", "Revisão do plano não encontrada.");

            var latest = await _revisions.GetLatestAsync(project.ProjectId, ct) ?? target;
            var read = await ReadPlanAsync(project.RootPath, target, ct);
            if (read.ErrorCode is not null)
                return PlanOutcome<EditPlanDto>.Fail(read.ErrorCode, read.ErrorMessage!);

            var source = read.Plan!;
            if (!IsFingerprintIntact(source))
            {
                _logger.LogWarning("Revert recusado: revisão {Revision} do plano {PlanId} adulterada (PLAN_TAMPERED)", source.Revision, source.PlanId);
                return PlanOutcome<EditPlanDto>.Fail(TamperedCode,
                    "A revisão de origem está adulterada: revert recusado. Gere um novo plano.");
            }

            var media = await _media.ListByProjectAsync(project.ProjectId, ct);
            var analyses = await LoadAnalysesAsync(project.ProjectId, media, ct);
            var now = _clock.GetUtcNow().UtcDateTime;
            // Persistido: só a validação estrutural no momento do revert (sem issues de estado externo).
            var structural = PlanValidation.Run(PlanMapping.ToDomain(source), media, analyses)
                .Where(i => !DerivedIssueCodes.Contains(i.Code))
                .ToList();
            var validation = PlanMapping.ToValidationDto(structural, now);

            var revision = await NextRevisionAsync(project.RootPath, target.PlanId, latest.Revision, ct);
            // TimeZoneId e perfil vêm da revisão alvo (são preservados pelo "with").
            var reverted = source with
            {
                Revision = revision,
                BaseRevision = target.Revision,
                CreatedAtUtc = now,
                Author = AuthorRevert,
                Validation = validation,
            };

            // O conteúdo copiado é o da alvo, então o fingerprint de entradas da alvo descreve o que ele reflete:
            // se as decisões mudaram desde a alvo, a revisão nasce com PLAN_OUTDATED (derivado na leitura).
            var conflict = await TryPersistAsync(project.RootPath, reverted, target.InputsFingerprint, ct);
            if (conflict is not null) return conflict;

            _logger.LogInformation("Plano {PlanId} revertido da revisão {Base} para a revisão {Revision}",
                reverted.PlanId, target.Revision, reverted.Revision);
            var derived = await DeriveValidationAsync(project.ProjectId, project.RootPath, reverted, target.InputsFingerprint, ct);
            return PlanOutcome<EditPlanDto>.Ok(reverted with { Validation = derived });
        }, ct);
    }

    // ------------------------------------------------------------------ Snapshot e persistência

    private async Task<PlanningInput> LoadInputAsync(
        Guid projectId,
        EditorialProfileDto profile,
        TimelineTimeBaseDto? timeBase,
        TimeZoneInfo zone,
        string zoneId,
        CancellationToken ct)
    {
        var media = await _media.ListByProjectAsync(projectId, ct);
        var pairs = PlanEligibility.Eligible(media)
            .Select(m => (Asset: m, Metadata: m.Metadata!))
            .ToList();
        var chronology = _chronology.BuildChronology(pairs);
        var analyses = await LoadAnalysesAsync(projectId, media, ct);
        var suggestions = await _suggestionsAndDecisions.ListSuggestionsByProjectAsync(projectId, ct);
        var decisions = await _suggestionsAndDecisions.ListDecisionsByProjectAsync(projectId, ct);

        return new PlanningInput(
            ProjectId: projectId,
            Profile: profile,
            RequestedTimeBase: timeBase,
            Media: media,
            Chronology: chronology,
            Analyses: analyses,
            Suggestions: suggestions,
            Decisions: decisions,
            DisplayZone: zone,
            TimeZoneId: zoneId);
    }

    private async Task<Dictionary<Guid, MediaAnalysisDto>> LoadAnalysesAsync(
        Guid projectId,
        IEnumerable<MediaAsset> media,
        CancellationToken ct)
    {
        var analyses = new Dictionary<Guid, MediaAnalysisDto>();
        foreach (var asset in media.Where(m => m.Kind is MediaKind.Video or MediaKind.Audio))
        {
            var analysis = await _analysis.GetMediaAnalysisAsync(projectId, asset.MediaId, ct);
            if (analysis is not null) analyses[asset.MediaId] = analysis;
        }
        return analyses;
    }

    /// <summary>
    /// Próxima revisão = maior número entre o índice e os arquivos rNNNN.json do plano + 1. Arquivo sem índice
    /// (órfão, p. ex. gravação interrompida após o arquivo) não bloqueia a geração: registra aviso e segue.
    /// </summary>
    private async Task<int> NextRevisionAsync(string projectRoot, Guid planId, int? indexLatest, CancellationToken ct)
    {
        var onDisk = await _artifacts.ListRevisionNumbersAsync(projectRoot, planId, ct);
        int diskMax = onDisk.Count == 0 ? 0 : onDisk.Max();
        int indexMax = indexLatest ?? 0;
        if (diskMax > indexMax)
        {
            _logger.LogWarning(
                "Plano {PlanId}: revisão {Revision} existe em disco sem linha de índice (órfã); a próxima revisão usa o número seguinte.",
                planId, diskMax);
        }
        return Math.Max(indexMax, diskMax) + 1;
    }

    /// <summary>
    /// Grava o artefato (sem sobrescrever) e depois o índice; se o índice falhar, remove o arquivo recém-criado.
    /// Retorna null em sucesso, ou PLAN_REVISION_CONFLICT quando o número já está ocupado no armazenamento.
    /// </summary>
    private async Task<PlanOutcome<EditPlanDto>?> TryPersistAsync(string projectRoot, EditPlanDto plan, string? inputsFingerprint, CancellationToken ct)
    {
        try
        {
            await PersistRevisionAsync(projectRoot, plan, inputsFingerprint, ct);
            return null;
        }
        catch (PlanRevisionConflictException)
        {
            _logger.LogWarning("Conflito ao gravar a revisão {Revision} do plano {PlanId}", plan.Revision, plan.PlanId);
            return PlanOutcome<EditPlanDto>.Fail("PLAN_REVISION_CONFLICT", ConflictMessage);
        }
    }

    private async Task PersistRevisionAsync(string projectRoot, EditPlanDto plan, string? inputsFingerprint, CancellationToken ct)
    {
        var json = PlanJson.Serialize(plan);
        await _artifacts.WriteRevisionAsync(projectRoot, plan.PlanId, plan.Revision, json, ct);
        try
        {
            await _revisions.AddAsync(PlanMapping.ToIndexEntry(plan) with { InputsFingerprint = inputsFingerprint }, ct);
        }
        catch
        {
            await _artifacts.DeleteUncommittedRevisionAsync(projectRoot, plan.PlanId, plan.Revision, CancellationToken.None);
            throw;
        }
    }

    private async Task<PlanRead> ReadPlanAsync(string projectRoot, PlanRevisionIndexEntry entry, CancellationToken ct)
    {
        var json = await _artifacts.ReadRevisionAsync(projectRoot, entry.PlanId, entry.Revision, ct);
        if (json is null) return new PlanRead(null, "PLAN_FILE_MISSING", MissingMessage);

        if (!PlanJson.TryDeserialize(json, out var plan)
            || plan!.PlanId != entry.PlanId || plan.Revision != entry.Revision)
        {
            _logger.LogWarning("Revisão {Revision} do plano {PlanId} com arquivo inválido (PLAN_FILE_CORRUPT)", entry.Revision, entry.PlanId);
            return new PlanRead(null, "PLAN_FILE_CORRUPT", CorruptMessage);
        }
        return new PlanRead(plan, null, null);
    }

    /// <summary>
    /// Lê uma revisão (sem gravar) e devolve o plano com a validação DERIVADA no momento. Usado por plan.get, pela
    /// regeneração idempotente e pelo revert. <paramref name="storedInputs"/>: fingerprint de entradas a comparar.
    /// </summary>
    private async Task<PlanOutcome<EditPlanDto>> ReadDerivedAsync(
        Guid projectId, string projectRoot, PlanRevisionIndexEntry entry, string? storedInputs, CancellationToken ct)
    {
        var read = await ReadPlanAsync(projectRoot, entry, ct);
        if (read.ErrorCode is not null) return PlanOutcome<EditPlanDto>.Fail(read.ErrorCode, read.ErrorMessage!);

        var validation = await DeriveValidationAsync(projectId, projectRoot, read.Plan!, storedInputs, ct);
        return PlanOutcome<EditPlanDto>.Ok(read.Plan! with { Validation = validation });
    }

    /// <summary>
    /// Validação derivada no momento, sem gravar nada: estrutura recalculada contra o estado atual das mídias
    /// (<see cref="PlanValidation.Run"/>, inclui MEDIA_CHANGED), PLAN_TAMPERED se o conteúdo não confere com o fingerprint,
    /// PLAN_OUTDATED se as entradas mudaram desde a revisão (só quando o índice tem o fingerprint de entradas) e
    /// SOURCE_FILE_MISSING por clipe cujo arquivo sumiu do disco. O JSON gravado não é usado para isso.
    /// </summary>
    private async Task<PlanValidationDto> DeriveValidationAsync(
        Guid projectId, string projectRoot, EditPlanDto plan, string? storedInputs, CancellationToken ct)
    {
        var issues = await DeriveIssuesAsync(projectId, projectRoot, plan, storedInputs, ct);
        return PlanMapping.ToValidationDto(issues, _clock.GetUtcNow().UtcDateTime);
    }

    private async Task<List<PlanIssue>> DeriveIssuesAsync(
        Guid projectId, string projectRoot, EditPlanDto plan, string? storedInputs, CancellationToken ct)
    {
        var media = await _media.ListByProjectAsync(projectId, ct);
        var analyses = await LoadAnalysesAsync(projectId, media, ct);
        var issues = PlanValidation.Run(PlanMapping.ToDomain(plan), media, analyses).ToList();

        if (!IsFingerprintIntact(plan))
        {
            _logger.LogWarning("Plano {PlanId} revisão {Revision}: fingerprint divergente (PLAN_TAMPERED)", plan.PlanId, plan.Revision);
            issues.Add(new PlanIssue(PlanIssueSeverity.Error, TamperedCode, TamperedMessage));
        }

        if (storedInputs is not null)
        {
            var suggestions = await _suggestionsAndDecisions.ListSuggestionsByProjectAsync(projectId, ct);
            var decisions = await _suggestionsAndDecisions.ListDecisionsByProjectAsync(projectId, ct);
            if (!string.Equals(storedInputs, InputsFingerprintOf(plan, media, suggestions, decisions), StringComparison.OrdinalIgnoreCase))
                issues.Add(new PlanIssue(PlanIssueSeverity.Warning, PlanOutdatedCode, PlanOutdatedMessage));
        }

        issues.AddRange(await MissingSourceIssuesAsync(projectRoot, plan, media, ct));
        return issues;
    }

    /// <summary>
    /// Remove do plano as issues derivadas (estado externo) antes de gravá-lo: o JSON guarda só a validação estrutural
    /// do momento, e IsValid do índice segue a mesma regra (não fica "pegajoso" após o arquivo voltar).
    /// </summary>
    private static EditPlanDto WithStructuralValidation(EditPlanDto plan)
    {
        var issues = plan.Validation.Issues.Where(i => !DerivedIssueCodes.Contains(i.Code)).ToList();
        return plan with { Validation = plan.Validation with { IsValid = !issues.Any(i => i.Severity == "error"), Issues = issues } };
    }

    /// <summary>Fingerprint das entradas para uma revisão: mídias elegíveis + estado efetivo de todas as sugestões + perfil/base/fuso.</summary>
    private static string InputsFingerprintOf(
        EditPlanDto plan,
        IEnumerable<MediaAsset> media,
        IReadOnlyList<EditSuggestion> suggestions,
        IReadOnlyList<EditDecision> decisions)
    {
        var effective = EffectiveSuggestionStatus.ResolveDetailed(suggestions, decisions).Values;
        return PlanInputsFingerprint.Compute(
            PlanValidation.MediaSetFingerprintOf(media), plan.Profile, plan.TimeBase, plan.TimeZoneId, effective);
    }

    /// <summary>
    /// Clipe cuja mídia não existe mais no disco (RootPath + RelativePath): um erro por clipe, com ClipId para a UI
    /// destacar o clipe. O disco é consultado uma vez por mídia. Mensagem sem caminho completo.
    /// </summary>
    private async Task<List<PlanIssue>> MissingSourceIssuesAsync(
        string projectRoot,
        EditPlanDto plan,
        IEnumerable<MediaAsset> media,
        CancellationToken ct)
    {
        var byId = media.DistinctBy(m => m.MediaId).ToDictionary(m => m.MediaId);
        var missing = new Dictionary<Guid, MediaAsset>();
        foreach (var mediaId in plan.Clips.Select(c => c.MediaId).Distinct())
        {
            if (!byId.TryGetValue(mediaId, out var asset)) continue; // mídia ausente já é MEDIA_MISSING
            if (!await _files.ExistsAsync(Path.Combine(projectRoot, asset.RelativePath), ct)) missing[mediaId] = asset;
        }

        return plan.Clips
            .Where(c => missing.ContainsKey(c.MediaId))
            .OrderBy(c => c.TimelineStartFrame).ThenBy(c => c.TrackId, StringComparer.Ordinal)
            .Select(c => new PlanIssue(PlanIssueSeverity.Error, SourceFileMissingCode,
                SourceFileMissingMessage + missing[c.MediaId].RelativePath, c.ClipId))
            .ToList();
    }

    /// <summary>Fingerprint do conteúdo (sem validação): confere com o gravado. Validação não entra no fingerprint.</summary>
    private static bool IsFingerprintIntact(EditPlanDto plan) =>
        string.Equals(PlanFingerprint.ComputePlan(PlanMapping.ToDomain(plan)), plan.PlanFingerprint, StringComparison.OrdinalIgnoreCase);

    /// <summary>Fuso informado (id Windows ou IANA) ou o fuso local da máquina quando omitido. Id inválido → falso.</summary>
    private static bool TryResolveZone(string? requested, out TimeZoneInfo zone, out string zoneId, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(requested))
        {
            zone = TimeZoneInfo.Local;
            zoneId = zone.Id;
            return true;
        }

        zoneId = requested.Trim();
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            error = "Fuso horário desconhecido (timeZoneId inválido).";
            return false;
        }
    }

    private static async Task<PlanOutcome<T>> WithProjectLockAsync<T>(Guid projectId, Func<Task<PlanOutcome<T>>> action, CancellationToken ct)
    {
        var gate = ProjectLocks.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
