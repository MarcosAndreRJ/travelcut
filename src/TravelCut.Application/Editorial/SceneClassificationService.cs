using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities.Editorial;

namespace TravelCut.Application.Editorial;

public interface ISceneClassificationService
{
    Task<EditorialSceneClassificationDto> UpsertClassificationAsync(UpsertSceneClassificationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<EditorialSceneClassificationDto>> BatchClassifyAsync(BatchClassifyRequest request, CancellationToken ct = default);
    Task<bool> DeleteClassificationAsync(Guid entryId, CancellationToken ct = default);
    Task<IReadOnlyList<EditorialSceneClassificationDto>> GetByProjectAsync(Guid projectId, CancellationToken ct = default);
    Task<IReadOnlyList<EditorialSceneClassificationDto>> GetByMediaAsync(Guid mediaId, CancellationToken ct = default);
    Task<EditorialSceneClassificationDto?> GetEffectiveClassificationAtAsync(Guid mediaId, double timestampSeconds, CancellationToken ct = default);
}

public sealed class SceneClassificationService : ISceneClassificationService
{
    private readonly ISceneClassificationRepository _repository;
    private readonly ILogger<SceneClassificationService> _logger;

    public SceneClassificationService(
        ISceneClassificationRepository repository,
        ILogger<SceneClassificationService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<EditorialSceneClassificationDto> UpsertClassificationAsync(
        UpsertSceneClassificationRequest request,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(request.ProjectId, out var projectId))
            throw new ArgumentException("ProjectId inválido.", nameof(request));

        if (!Guid.TryParse(request.MediaId, out var mediaId))
            throw new ArgumentException("MediaId inválido.", nameof(request));

        Guid entryId = Guid.TryParse(request.EntryId, out var existingId) ? existingId : Guid.NewGuid();

        double start = request.StartSeconds ?? 0.0;
        double end = request.EndSeconds ?? 0.0;

        if (!request.IsFullVideo && end < start)
        {
            throw new ArgumentException("Intervalo inválido: endSeconds não pode ser menor que startSeconds.", nameof(request));
        }

        var entry = new SceneClassificationEntry
        {
            EntryId = entryId,
            ProjectId = projectId,
            MediaId = mediaId,
            StartSeconds = request.IsFullVideo ? 0.0 : start,
            EndSeconds = request.IsFullVideo ? 0.0 : end,
            IsFullVideo = request.IsFullVideo,
            Environment = ParseEnvironment(request.Environment),
            Interaction = ParseInteraction(request.Interaction),
            Framing = ParseFraming(request.Framing),
            Movement = ParseMovement(request.Movement),
            EditorialProfile = ParseProfile(request.EditorialProfile),
            Notes = request.Notes ?? string.Empty,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await _repository.SaveAsync(entry, ct);
        _logger.LogInformation("Classificação de cena salva: EntryId={EntryId}, MediaId={MediaId}, FullVideo={IsFullVideo}",
            entry.EntryId, entry.MediaId, entry.IsFullVideo);

        return ToDto(entry);
    }

    public async Task<IReadOnlyList<EditorialSceneClassificationDto>> BatchClassifyAsync(
        BatchClassifyRequest request,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(request.ProjectId, out var projectId))
            throw new ArgumentException("ProjectId inválido.", nameof(request));

        var results = new List<EditorialSceneClassificationDto>();
        var env = ParseEnvironment(request.Environment);
        var interaction = ParseInteraction(request.Interaction);
        var framing = ParseFraming(request.Framing);
        var movement = ParseMovement(request.Movement);
        var profile = ParseProfile(request.EditorialProfile);

        foreach (var mediaIdStr in request.MediaIds)
        {
            if (!Guid.TryParse(mediaIdStr, out var mediaId)) continue;

            var entry = new SceneClassificationEntry
            {
                EntryId = Guid.NewGuid(),
                ProjectId = projectId,
                MediaId = mediaId,
                StartSeconds = 0.0,
                EndSeconds = 0.0,
                IsFullVideo = true,
                Environment = env,
                Interaction = interaction,
                Framing = framing,
                Movement = movement,
                EditorialProfile = profile,
                Notes = request.Notes ?? string.Empty,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            await _repository.SaveAsync(entry, ct);
            results.Add(ToDto(entry));
        }

        return results;
    }

    public async Task<bool> DeleteClassificationAsync(Guid entryId, CancellationToken ct = default)
    {
        var existing = await _repository.GetByIdAsync(entryId, ct);
        if (existing is null) return false;

        await _repository.DeleteAsync(entryId, ct);
        return true;
    }

    public async Task<IReadOnlyList<EditorialSceneClassificationDto>> GetByProjectAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var entries = await _repository.ListByProjectAsync(projectId, ct);
        return entries.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<EditorialSceneClassificationDto>> GetByMediaAsync(
        Guid mediaId,
        CancellationToken ct = default)
    {
        var entries = await _repository.ListByMediaAsync(mediaId, ct);
        return entries.Select(ToDto).ToList();
    }

    public async Task<EditorialSceneClassificationDto?> GetEffectiveClassificationAtAsync(
        Guid mediaId,
        double timestampSeconds,
        CancellationToken ct = default)
    {
        var entries = await _repository.ListByMediaAsync(mediaId, ct);
        if (entries.Count == 0) return null;

        var intervalMatch = entries
            .Where(e => !e.IsFullVideo && e.Overlaps(timestampSeconds, timestampSeconds + 0.001))
            .OrderByDescending(e => e.UpdatedAtUtc)
            .FirstOrDefault();

        if (intervalMatch != null)
        {
            return ToDto(intervalMatch);
        }

        var fullVideo = entries
            .Where(e => e.IsFullVideo)
            .OrderByDescending(e => e.UpdatedAtUtc)
            .FirstOrDefault();

        return fullVideo != null ? ToDto(fullVideo) : null;
    }

    public static EditorialSceneClassificationDto ToDto(SceneClassificationEntry entry) =>
        new(
            entry.EntryId.ToString(),
            entry.ProjectId.ToString(),
            entry.MediaId.ToString(),
            entry.StartSeconds,
            entry.EndSeconds,
            entry.IsFullVideo,
            entry.Environment switch
            {
                SceneEnvironment.Interior => SceneClassificationConstants.EnvInterior,
                SceneEnvironment.Exterior => SceneClassificationConstants.EnvExterior,
                SceneEnvironment.Mixed => SceneClassificationConstants.EnvMixed,
                _ => SceneClassificationConstants.EnvUnclassified
            },
            entry.Interaction switch
            {
                SceneInteraction.DirectToCamera => SceneClassificationConstants.InteractionDirectToCamera,
                SceneInteraction.Dialogue => SceneClassificationConstants.InteractionDialogue,
                SceneInteraction.Interview => SceneClassificationConstants.InteractionInterview,
                SceneInteraction.Narration => SceneClassificationConstants.InteractionNarration,
                SceneInteraction.Broll => SceneClassificationConstants.InteractionBroll,
                SceneInteraction.Landscape => SceneClassificationConstants.InteractionLandscape,
                _ => SceneClassificationConstants.InteractionOther
            },
            entry.Framing switch
            {
                SceneFraming.Wide => SceneClassificationConstants.FramingWide,
                SceneFraming.Medium => SceneClassificationConstants.FramingMedium,
                SceneFraming.CloseUp => SceneClassificationConstants.FramingCloseUp,
                _ => SceneClassificationConstants.FramingUnspecified
            },
            entry.Movement switch
            {
                SceneMovement.Static => SceneClassificationConstants.MovementStatic,
                SceneMovement.Moving => SceneClassificationConstants.MovementMoving,
                _ => SceneClassificationConstants.MovementUnspecified
            },
            entry.EditorialProfile switch
            {
                SceneEditorialProfile.Natural => SceneClassificationConstants.ProfileNatural,
                SceneEditorialProfile.Dynamic => SceneClassificationConstants.ProfileDynamic,
                SceneEditorialProfile.Cinematic => SceneClassificationConstants.ProfileCinematic,
                _ => SceneClassificationConstants.ProfileNatural
            },
            entry.Notes ?? string.Empty,
            entry.CreatedAtUtc.ToString("o"),
            entry.UpdatedAtUtc.ToString("o")
        );

    private static SceneEnvironment ParseEnvironment(string? val) => (val?.ToLowerInvariant()) switch
    {
        SceneClassificationConstants.EnvInterior or "interior" => SceneEnvironment.Interior,
        SceneClassificationConstants.EnvExterior or "exterior" => SceneEnvironment.Exterior,
        SceneClassificationConstants.EnvMixed or "mixed" => SceneEnvironment.Mixed,
        _ => SceneEnvironment.Unclassified
    };

    private static SceneInteraction ParseInteraction(string? val) => (val?.ToLowerInvariant()) switch
    {
        SceneClassificationConstants.InteractionDirectToCamera or "direct_to_camera" => SceneInteraction.DirectToCamera,
        SceneClassificationConstants.InteractionDialogue or "dialogue" => SceneInteraction.Dialogue,
        SceneClassificationConstants.InteractionInterview or "interview" => SceneInteraction.Interview,
        SceneClassificationConstants.InteractionNarration or "narration" => SceneInteraction.Narration,
        SceneClassificationConstants.InteractionBroll or "broll" => SceneInteraction.Broll,
        SceneClassificationConstants.InteractionLandscape or "landscape" => SceneInteraction.Landscape,
        _ => SceneInteraction.Other
    };

    private static SceneFraming ParseFraming(string? val) => (val?.ToLowerInvariant()) switch
    {
        SceneClassificationConstants.FramingWide or "wide" => SceneFraming.Wide,
        SceneClassificationConstants.FramingMedium or "medium" => SceneFraming.Medium,
        SceneClassificationConstants.FramingCloseUp or "close_up" => SceneFraming.CloseUp,
        _ => SceneFraming.Unspecified
    };

    private static SceneMovement ParseMovement(string? val) => (val?.ToLowerInvariant()) switch
    {
        SceneClassificationConstants.MovementStatic or "static" => SceneMovement.Static,
        SceneClassificationConstants.MovementMoving or "moving" => SceneMovement.Moving,
        _ => SceneMovement.Unspecified
    };

    private static SceneEditorialProfile ParseProfile(string? val) => (val?.ToLowerInvariant()) switch
    {
        SceneClassificationConstants.ProfileDynamic or "dynamic" => SceneEditorialProfile.Dynamic,
        SceneClassificationConstants.ProfileCinematic or "cinematic" => SceneEditorialProfile.Cinematic,
        _ => SceneEditorialProfile.Natural
    };
}
