using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;

namespace TravelCut.Application.Editor;

/// <summary>
/// Use cases do editor (F08, contrato editor/1): capacidades, dry-run (prepareApply), aplicação (apply) e histórico.
/// Nunca abre projeto existente do usuário: o adaptador cria um projeto novo a partir do manifesto.
/// Erros são devolvidos como <see cref="PlanOutcome{T}"/> com código estável (sem caminhos absolutos).
/// </summary>
public sealed class EditorUseCases
{
    public const string ModeDavinci = "davinci";
    public const string ModeMock = "mock";
    public const string ApplySchemaVersion = "editor-apply/1";
    public const string ErrorSeverity = "error";

    public const string EditorUnavailableCode = "EDITOR_UNAVAILABLE";
    public const string NoClipsCode = "NO_CLIPS";
    public const string NoPlanCode = "NO_PLAN";
    public const string ConfirmationRequiredCode = "CONFIRMATION_REQUIRED";
    public const string VersionConflictCode = "PROJECT_VERSION_CONFLICT";
    public const string IdempotencyKeyReusedCode = "IDEMPOTENCY_KEY_REUSED";
    public const string InvalidIdempotencyKeyCode = "INVALID_IDEMPOTENCY_KEY";
    public const string BridgeFailedCode = "EDITOR_BRIDGE_FAILED";
    public const string CancelledCode = "EDITOR_CANCELLED";
    public const string EditorErrorCode = "EDITOR_ERROR";
    public const string OperationInProgressCode = "OPERATION_IN_PROGRESS";

    /// <summary>Nome da trava entre processos da aplicação no editor (<c>3-Trabalho/.locks/editor-apply.lock</c>).</summary>
    public const string ApplyOperation = "editor-apply";

    /// <summary>Espera máxima pela trava (em processo e entre processos) antes de OPERATION_IN_PROGRESS.</summary>
    public static readonly TimeSpan ApplyLockWait = TimeSpan.FromSeconds(2);

    /// <summary>Tempo máximo de uma aplicação (o adaptador DaVinci deve respeitar em <see cref="EditorApplyOptions.Timeout"/>).</summary>
    public static readonly TimeSpan ApplyTimeout = TimeSpan.FromMinutes(10);

    private const int MaxMessageLength = 500;

    // \z (não $): $ aceitaria uma quebra de linha final na chave.
    private static readonly Regex IdempotencyKeyPattern = new(@"^[A-Za-z0-9_-]{1,100}\z", RegexOptions.CultureInvariant);

    /// <summary>Códigos de issue do plano que bloqueiam a aplicação mesmo quando o severity não é "error".</summary>
    private static readonly HashSet<string> BlockingPlanCodes =
    [
        PlanningUseCases.PlanOutdatedCode,
        PlanningUseCases.SourceFileMissingCode,
        PlanningUseCases.TamperedCode,
    ];

    // Gate em processo por raiz de projeto, com contagem de uso: a entrada some quando ninguém a usa.
    private static readonly Dictionary<string, ApplyGate> ApplyGates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object ApplyGatesSync = new();

    private readonly IProjectRepository _projects;
    private readonly IMediaRepository _media;
    private readonly PlanningUseCases _planning;
    private readonly IEditorAdapter _adapter;
    private readonly IEditorArtifactStore _store;
    private readonly IFileExistence _files;
    private readonly IProjectOperationLock _locks;
    private readonly TimeProvider _clock;
    private readonly ILogger<EditorUseCases> _logger;

    public EditorUseCases(
        IProjectRepository projects,
        IMediaRepository media,
        PlanningUseCases planning,
        IEditorAdapter adapter,
        IEditorArtifactStore store,
        IFileExistence files,
        IProjectOperationLock locks,
        TimeProvider clock,
        ILogger<EditorUseCases> logger)
    {
        _projects = projects;
        _media = media;
        _planning = planning;
        _adapter = adapter;
        _store = store;
        _files = files;
        _locks = locks;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Estado do adaptador (mock ou DaVinci) sem efeitos colaterais.</summary>
    public Task<EditorCapabilitiesDto> CapabilitiesAsync(CancellationToken ct = default) =>
        _adapter.DetectCapabilitiesAsync(ct);

    /// <summary>
    /// Dry-run: monta o manifesto (sempre gravado em 3-Trabalho/editor), consulta capacidades e lista os bloqueios.
    /// Não chama o adaptador de escrita.
    /// </summary>
    public async Task<PlanOutcome<EditorApplyPreviewDto>> PrepareApplyAsync(PrepareEditorApplyRequest request, CancellationToken ct = default)
    {
        var prepared = await PrepareCoreAsync(request.ProjectId, request.Revision, ct);
        if (!prepared.Success)
            return PlanOutcome<EditorApplyPreviewDto>.Fail(prepared.ErrorCode!, prepared.ErrorMessage!);

        // Dry-run grava o manifesto (artefato derivado, fallback para aplicação manual).
        var p = prepared.Value!;
        var manifestRelative = await _store.WriteAssemblyAsync(p.ProjectRoot, p.Assembly, ct);
        return PlanOutcome<EditorApplyPreviewDto>.Ok(BuildPreview(p, manifestRelative));
    }

    /// <summary>
    /// Aplica a montagem. Ordem: chave de idempotência → projeto → gate em processo → trava entre processos
    /// (<c>editor-apply</c>, até <see cref="ApplyLockWait"/>; ocupada → OPERATION_IN_PROGRESS sem chamar o adaptador) →
    /// dry-run refeito → fingerprint → bloqueios → confirmação de troca de projeto → reaproveitamento por idempotência
    /// (sempre DEPOIS do lock, para que outro processo veja o registro) → adaptador → registro (sempre, inclusive ERROR).
    /// </summary>
    public async Task<PlanOutcome<EditorApplyResultDto>> ApplyAsync(ApplyEditorPlanRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<EditorApplyResultDto>.Fail("INVALID_ARGUMENT", "ID de projeto inválido.");
        if (request.IdempotencyKey is null || !IdempotencyKeyPattern.IsMatch(request.IdempotencyKey))
            return PlanOutcome<EditorApplyResultDto>.Fail(InvalidIdempotencyKeyCode,
                "Chave de idempotência inválida (1 a 100 caracteres: letras, números, '_' ou '-').");

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null || string.IsNullOrWhiteSpace(project.RootPath))
            return PlanOutcome<EditorApplyResultDto>.Fail("PROJECT_NOT_FOUND", PlanningUseCases.NoProjectMessage);
        // Mesma chave da trava de arquivo: a raiz do projeto (não o id, que pode repetir em bancos/fixtures diferentes).
        var root = Path.GetFullPath(project.RootPath);

        var gate = EnterApplyGate(root);
        try
        {
            if (!await gate.Semaphore.WaitAsync(ApplyLockWait, ct))
                return OperationInProgress();

            try
            {
                return await ApplyWithFileLockAsync(request, root, ct);
            }
            finally
            {
                gate.Semaphore.Release();
            }
        }
        finally
        {
            LeaveApplyGate(root, gate);
        }
    }

    /// <summary>
    /// Trava entre processos. Ocupada → OPERATION_IN_PROGRESS (sem adaptador); falha estrutural da trava
    /// (pasta do projeto ausente, pasta de travas inacessível, E/S) → código estável, sem caminho na mensagem.
    /// </summary>
    private async Task<PlanOutcome<EditorApplyResultDto>> ApplyWithFileLockAsync(ApplyEditorPlanRequest request, string root, CancellationToken ct)
    {
        IAsyncDisposable? fileLock;
        try
        {
            fileLock = await _locks.TryAcquireAsync(root, ApplyOperation, ApplyLockWait, ct);
        }
        catch (ProjectLockException ex)
        {
            _logger.LogWarning("Trava de aplicação indisponível: código {LockErrorCode}", ex.Code);
            return PlanOutcome<EditorApplyResultDto>.Fail(ex.Code, ProjectLockMessage(ex.Code));
        }

        if (fileLock is null)
            return OperationInProgress();

        await using (fileLock)
        {
            return await ApplyLockedAsync(request, ct);
        }
    }

    private static string ProjectLockMessage(string code) => code switch
    {
        ProjectLockException.ProjectRootMissingCode => "A pasta do projeto não foi encontrada.",
        ProjectLockException.LockAccessDeniedCode => "Sem permissão para usar a trava de aplicação do projeto.",
        _ => "Não foi possível usar a trava de aplicação do projeto (pasta de travas indisponível)."
    };

    private static PlanOutcome<EditorApplyResultDto> OperationInProgress() =>
        PlanOutcome<EditorApplyResultDto>.Fail(OperationInProgressCode,
            "outra aplicação deste projeto está em andamento (app ou MCP).");

    private static ApplyGate EnterApplyGate(string root)
    {
        lock (ApplyGatesSync)
        {
            if (!ApplyGates.TryGetValue(root, out var gate))
            {
                gate = new ApplyGate();
                ApplyGates[root] = gate;
            }
            gate.Users++;
            return gate;
        }
    }

    private static void LeaveApplyGate(string root, ApplyGate gate)
    {
        lock (ApplyGatesSync)
        {
            gate.Users--;
            if (gate.Users == 0 && ApplyGates.TryGetValue(root, out var current) && ReferenceEquals(current, gate))
            {
                ApplyGates.Remove(root);
                gate.Semaphore.Dispose();
            }
        }
    }

    private sealed class ApplyGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int Users;
    }

    /// <summary>Registros de aplicação do projeto, mais recente primeiro.</summary>
    public async Task<PlanOutcome<EditorApplicationsResponse>> ListApplicationsAsync(Guid projectId, CancellationToken ct = default)
    {
        if (projectId == Guid.Empty)
            return PlanOutcome<EditorApplicationsResponse>.Fail("INVALID_ARGUMENT", "ID de projeto inválido.");

        var project = await _projects.GetByIdAsync(projectId, ct);
        if (project is null || string.IsNullOrWhiteSpace(project.RootPath))
            return PlanOutcome<EditorApplicationsResponse>.Fail("PROJECT_NOT_FOUND", PlanningUseCases.NoProjectMessage);

        var all = await _store.ListApplicationsAsync(Path.GetFullPath(project.RootPath), ct);
        var list = all
            .Where(a => a.ProjectId == projectId)
            .OrderByDescending(a => a.StartedAtUtc)
            .ToList();
        return PlanOutcome<EditorApplicationsResponse>.Ok(new EditorApplicationsResponse(list));
    }

    // ------------------------------------------------------------------ Apply

    private async Task<PlanOutcome<EditorApplyResultDto>> ApplyLockedAsync(ApplyEditorPlanRequest request, CancellationToken ct)
    {
        var prepared = await PrepareCoreAsync(request.ProjectId, request.Revision, ct);
        if (!prepared.Success)
            return PlanOutcome<EditorApplyResultDto>.Fail(prepared.ErrorCode!, prepared.ErrorMessage!);

        var p = prepared.Value!;
        var assembly = p.Assembly;

        // Apply obsoleto: recusa sem gravar nada (o r####.assembly.json da montagem atual não é reescrito).
        if (!string.Equals(request.AssemblyFingerprint, assembly.AssemblyFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return PlanOutcome<EditorApplyResultDto>.Fail(VersionConflictCode,
                "A montagem mudou desde o dry-run. Prepare a aplicação novamente.");
        }

        // Fingerprint confirmado: o manifesto é gravado (também quando bloqueado, como fallback para aplicação manual).
        if (p.BlockingReasons.Count > 0)
        {
            await _store.WriteAssemblyAsync(p.ProjectRoot, assembly, ct);
            var first = p.BlockingReasons[0];
            return PlanOutcome<EditorApplyResultDto>.Fail(first, BlockingMessage(first));
        }

        if (_adapter.Mode == ModeDavinci && !request.ConfirmProjectSwitch)
        {
            return PlanOutcome<EditorApplyResultDto>.Fail(ConfirmationRequiredCode,
                "Confirme a troca do projeto aberto no DaVinci Resolve: um projeto novo será criado.");
        }

        // Idempotência: mesma chave com outra montagem é erro; mesma montagem já aplicada com resultado útil é reaproveitada.
        var sameKey = (await _store.ListApplicationsAsync(p.ProjectRoot, ct))
            .Where(a => a.ProjectId == request.ProjectId && a.IdempotencyKey == request.IdempotencyKey)
            .OrderByDescending(a => a.StartedAtUtc)
            .ToList();

        if (sameKey.Any(a => !string.Equals(a.AssemblyFingerprint, assembly.AssemblyFingerprint, StringComparison.OrdinalIgnoreCase)))
        {
            return PlanOutcome<EditorApplyResultDto>.Fail(IdempotencyKeyReusedCode,
                "Esta chave de idempotência já foi usada com outra montagem.");
        }

        // Só reaproveita resultados que já dizem algo (simulado ou real). ERROR e UNAVAILABLE são tentados de novo.
        var reusable = sameKey.FirstOrDefault(a => a.Status is EditorResultStatus.Simulated
            or EditorResultStatus.ReadyReal or EditorResultStatus.PartialReal);
        if (reusable is not null)
        {
            _logger.LogInformation("Aplicação reaproveitada por idempotência (plano {PlanId} r{Revision})", assembly.PlanId, assembly.Revision);
            return PlanOutcome<EditorApplyResultDto>.Ok(reusable with { ReusedPrevious = true });
        }

        var manifestRelative = await _store.WriteAssemblyAsync(p.ProjectRoot, assembly, ct);
        var manifestAbsolute = Path.GetFullPath(Path.Combine(p.ProjectRoot, manifestRelative.Replace('/', Path.DirectorySeparatorChar)));

        var applicationId = Guid.NewGuid();
        var startedUtc = _clock.GetUtcNow().UtcDateTime;
        var snapshotAbsolute = _adapter.Mode == ModeDavinci
            ? _store.GetSnapshotAbsolutePath(p.ProjectRoot, assembly.PlanId, assembly.EditorProjectName)
            : null;

        EditorAdapterApplyResult adapterResult;
        try
        {
            // A bridge exporta o .drp direto no caminho; a pasta precisa existir (dentro do try: falha vira registro ERROR).
            if (snapshotAbsolute is not null) Directory.CreateDirectory(Path.GetDirectoryName(snapshotAbsolute)!);
            adapterResult = await _adapter.ApplyAssemblyAsync(
                assembly, new EditorApplyOptions(snapshotAbsolute, ApplyTimeout, manifestAbsolute), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            adapterResult = Failed(CancelledCode, "Aplicação cancelada antes de terminar; confira o projeto no editor.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha no adaptador do editor ao aplicar plano {PlanId} r{Revision}", assembly.PlanId, assembly.Revision);
            adapterResult = Failed(BridgeFailedCode, "Falha na comunicação com o adaptador do editor.");
        }

        // CancellationToken.None: depois do resultado do adaptador (inclusive cancelado) o registro precisa ser gravado.
        var snapshotRelative = await ResolveSnapshotRelativeAsync(p.ProjectRoot, adapterResult.SnapshotAbsolutePath, CancellationToken.None);
        var finishedUtc = _clock.GetUtcNow().UtcDateTime;

        var isError = adapterResult.Status == EditorResultStatus.Error;
        var record = new EditorApplyResultDto(
            SchemaVersion: ApplySchemaVersion,
            ApplicationId: applicationId,
            Status: adapterResult.Status,
            AdapterMode: _adapter.Mode,
            // Simulado = modo mock OU resultado real do adaptador marcado SIMULATED (não o dry-run).
            IsSimulated: _adapter.Mode == ModeMock || adapterResult.Status == EditorResultStatus.Simulated,
            ProjectId: request.ProjectId,
            PlanId: assembly.PlanId,
            Revision: assembly.Revision,
            AssemblyFingerprint: assembly.AssemblyFingerprint,
            IdempotencyKey: request.IdempotencyKey,
            EditorProjectName: assembly.EditorProjectName,
            TimelineName: assembly.TimelineName,
            EditorVersion: adapterResult.EditorVersion,
            Steps: adapterResult.Steps.ToList(),
            ClipMappings: adapterResult.ClipMappings.ToList(),
            Rejected: assembly.Rejected.Concat(adapterResult.Rejected).ToList(),
            Warnings: assembly.Warnings.Concat(adapterResult.Warnings).ToList(),
            SnapshotRelativePath: snapshotRelative,
            RecordRelativePath: EditorStoragePaths.RelativeApplicationPath(assembly.PlanId, applicationId),
            StartedAtUtc: startedUtc,
            FinishedAtUtc: finishedUtc,
            ErrorCode: isError ? adapterResult.ErrorCode ?? EditorErrorCode : adapterResult.ErrorCode,
            ErrorMessage: SafeMessage(adapterResult.ErrorMessage, p.ProjectRoot),
            ReusedPrevious: false);

        // Registro sempre gravado (inclusive ERROR): é a trilha do que foi tentado no editor.
        // CancellationToken.None: o editor já pode ter sido alterado; perder o registro esconderia isso.
        try
        {
            await _store.WriteApplicationAsync(p.ProjectRoot, record, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Registro da aplicação {ApplicationId} não pôde ser gravado", record.ApplicationId);
            record = record with
            {
                Warnings = [.. record.Warnings, new EditorWarningDto("RECORD_NOT_SAVED",
                    "O resultado no editor não pôde ser registrado em 3-Trabalho/editor. Confira o projeto no DaVinci antes de repetir.", null)],
            };
        }

        _logger.LogInformation("Plano {PlanId} r{Revision} aplicado no editor com status {Status}",
            assembly.PlanId, assembly.Revision, record.Status);
        return PlanOutcome<EditorApplyResultDto>.Ok(record);
    }

    // ------------------------------------------------------------------ Dry-run (núcleo compartilhado)

    /// <summary>Montagem calculada e bloqueios. NÃO grava nada: a gravação do manifesto é decisão de quem chama.</summary>
    private sealed record PreparedAssembly(
        string ProjectRoot,
        EditorAssemblyDto Assembly,
        EditorCapabilitiesDto Capabilities,
        List<string> BlockingReasons);

    /// <summary>
    /// Plano → mídias → manifesto (em memória) → capacidades → bloqueios. Usado por prepareApply e apply.
    /// </summary>
    private async Task<PlanOutcome<PreparedAssembly>> PrepareCoreAsync(Guid projectId, int? revision, CancellationToken ct)
    {
        if (projectId == Guid.Empty)
            return PlanOutcome<PreparedAssembly>.Fail("INVALID_ARGUMENT", "ID de projeto inválido.");
        if (revision is < 1)
            return PlanOutcome<PreparedAssembly>.Fail("INVALID_ARGUMENT", "Número de revisão inválido.");

        var project = await _projects.GetByIdAsync(projectId, ct);
        if (project is null || string.IsNullOrWhiteSpace(project.RootPath))
            return PlanOutcome<PreparedAssembly>.Fail("PROJECT_NOT_FOUND", PlanningUseCases.NoProjectMessage);

        var root = Path.GetFullPath(project.RootPath);

        var planOutcome = await _planning.GetAsync(new GetPlanRequest(projectId, revision), ct);
        if (!planOutcome.Success)
            return PlanOutcome<PreparedAssembly>.Fail(planOutcome.ErrorCode!, planOutcome.ErrorMessage ?? "Falha ao ler o plano.");
        var plan = planOutcome.Value;
        if (plan is null)
            return PlanOutcome<PreparedAssembly>.Fail(NoPlanCode, "Não há plano editorial neste projeto. Gere um plano antes de preparar a montagem.");

        var sources = await BuildMediaSourcesAsync(projectId, root, plan, ct);
        var built = EditorAssemblyBuilder.Build(plan, sources, project.Name, _clock.GetUtcNow().UtcDateTime);
        if (built.Assembly is null)
            return PlanOutcome<PreparedAssembly>.Fail(built.ErrorCode ?? EditorAssemblyBuilder.InvalidTimebaseCode,
                built.ErrorMessage ?? "Não foi possível montar o manifesto.");
        var assembly = built.Assembly;

        var caps = await _adapter.DetectCapabilitiesAsync(ct);
        var blocking = BlockingReasons(plan, assembly, caps);

        _logger.LogInformation("Montagem preparada: plano {PlanId} r{Revision}, {ClipCount} clipe(s), {RejectedCount} rejeição(ões), {BlockingCount} bloqueio(s)",
            assembly.PlanId, assembly.Revision, assembly.Clips.Count, assembly.Rejected.Count, blocking.Count);

        return PlanOutcome<PreparedAssembly>.Ok(new PreparedAssembly(root, assembly, caps, blocking));
    }

    private EditorApplyPreviewDto BuildPreview(PreparedAssembly p, string manifestRelativePath)
    {
        var assembly = p.Assembly;
        var mediaPaths = assembly.Media.ToDictionary(m => m.MediaId, m => m.RelativePath);
        return new EditorApplyPreviewDto(
            Status: EditorResultStatus.DryRun,
            PlanId: assembly.PlanId,
            Revision: assembly.Revision,
            PlanFingerprint: assembly.PlanFingerprint,
            AssemblyFingerprint: assembly.AssemblyFingerprint,
            EditorProjectName: assembly.EditorProjectName,
            TimelineName: assembly.TimelineName,
            TimelineFrameRate: assembly.TimelineFrameRate,
            Clips: assembly.Clips.Select(c => new EditorClipPreviewDto(
                c.ClipId,
                mediaPaths.GetValueOrDefault(c.MediaId, string.Empty),
                c.SourceStartFrame,
                c.SourceEndFrameExclusive,
                c.RecordFrame,
                c.DurationFrames)).ToList(),
            Rejected: assembly.Rejected,
            Warnings: assembly.Warnings,
            ManifestRelativePath: manifestRelativePath,
            Capabilities: p.Capabilities,
            CanApply: p.BlockingReasons.Count == 0,
            BlockingReasons: p.BlockingReasons,
            RequiresProjectSwitchConfirmation: _adapter.Mode == ModeDavinci);
    }

    /// <summary>
    /// Mídias usadas pelo plano com caminho absoluto validado (dentro de 1-Bruto ou 2-Assets). Mídia fora dessas pastas
    /// fica ausente do mapa, e o builder a rejeita como MEDIA_NOT_FOUND (sem expor o caminho).
    /// </summary>
    private async Task<Dictionary<Guid, EditorMediaSource>> BuildMediaSourcesAsync(
        Guid projectId, string root, EditPlanDto plan, CancellationToken ct)
    {
        var assets = await _media.ListByProjectAsync(projectId, ct);
        var byId = assets.DistinctBy(a => a.MediaId).ToDictionary(a => a.MediaId);
        var sources = new Dictionary<Guid, EditorMediaSource>();

        foreach (var mediaId in plan.Clips.Select(c => c.MediaId).Distinct())
        {
            if (!byId.TryGetValue(mediaId, out var asset)) continue;

            string absolute;
            try
            {
                absolute = Path.GetFullPath(Path.Combine(root, asset.RelativePath));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                _logger.LogWarning("Mídia {MediaId} com caminho inválido; não entra na montagem", mediaId);
                continue;
            }

            if (!IsInsideSourceFolder(root, absolute))
            {
                _logger.LogWarning("Mídia {MediaId} fora de 1-Bruto/2-Assets; não entra na montagem", mediaId);
                continue;
            }

            var metadata = asset.Metadata;
            var fps = metadata is { MetadataStatus: MediaMetadataStatus.Ok }
                ? EditorTimebases.SourceFpsFromFrameRate(metadata.FrameRate)
                : null;
            long durationUs = metadata is null || !double.IsFinite(metadata.DurationSeconds)
                ? 0
                : (long)Math.Round(metadata.DurationSeconds * 1_000_000, MidpointRounding.ToEven);

            sources[mediaId] = new EditorMediaSource(
                mediaId,
                asset.RelativePath.Replace('\\', '/'),
                absolute,
                fps?.Num ?? 0,
                fps?.Den ?? 0,
                metadata?.IsVariableFrameRate ?? false,
                durationUs);
        }

        return sources;
    }

    private static bool IsInsideSourceFolder(string root, string absolutePath)
    {
        foreach (var folder in new[] { ImportPathRules.BrutoFolder, ImportPathRules.AssetsFolder })
        {
            var baseDir = Path.GetFullPath(Path.Combine(root, folder)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (absolutePath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Bloqueios: editor indisponível, issues de erro ou de estado (PLAN_OUTDATED etc.), ou nenhum clipe aplicável.</summary>
    private static List<string> BlockingReasons(EditPlanDto plan, EditorAssemblyDto assembly, EditorCapabilitiesDto caps)
    {
        var reasons = new List<string>();

        bool applySupported = caps.Operations.Any(o => o.Operation == EditorOperations.ApplyEditPlan && o.Supported);
        if (!caps.IsAvailable || !applySupported) reasons.Add(EditorUnavailableCode);

        foreach (var issue in plan.Validation.Issues)
        {
            if (string.Equals(issue.Severity, ErrorSeverity, StringComparison.OrdinalIgnoreCase) || BlockingPlanCodes.Contains(issue.Code))
                reasons.Add(issue.Code);
        }

        if (assembly.Clips.Count == 0) reasons.Add(NoClipsCode);

        return reasons.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string BlockingMessage(string code) => code switch
    {
        EditorUnavailableCode => "Editor indisponível neste ambiente. O manifesto foi gravado para aplicação manual.",
        PlanningUseCases.PlanOutdatedCode => "O plano mudou desde esta revisão. Gere um novo plano.",
        PlanningUseCases.SourceFileMissingCode => "Há arquivo de mídia ausente em disco.",
        PlanningUseCases.TamperedCode => "A revisão do plano não confere com o fingerprint gravado.",
        NoClipsCode => "Nenhum clipe aplicável na montagem.",
        _ => $"Pendência do plano impede a aplicação ({code})."
    };

    private async Task<string?> ResolveSnapshotRelativeAsync(string root, string? snapshotAbsolute, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(snapshotAbsolute)) return null;
        var relative = _store.TryGetRelativePath(root, snapshotAbsolute);
        if (relative is null) return null;
        return await _files.ExistsAsync(snapshotAbsolute, ct) ? relative : null;
    }

    private static EditorAdapterApplyResult Failed(string code, string message) => new(
        Status: EditorResultStatus.Error,
        EditorVersion: null,
        Steps: [],
        ClipMappings: [],
        Rejected: [],
        Warnings: [],
        SnapshotAbsolutePath: null,
        ErrorCode: code,
        ErrorMessage: message);

    /// <summary>Remove o caminho da raiz de mensagens do adaptador (nunca expor caminho absoluto) e limita o tamanho.</summary>
    private static string? SafeMessage(string? message, string root)
    {
        if (string.IsNullOrEmpty(message)) return message;
        var cleaned = message
            .Replace(root, "<projeto>", StringComparison.OrdinalIgnoreCase)
            .Replace(root.Replace('\\', '/'), "<projeto>", StringComparison.OrdinalIgnoreCase);
        return cleaned.Length > MaxMessageLength ? cleaned[..MaxMessageLength] : cleaned;
    }
}
