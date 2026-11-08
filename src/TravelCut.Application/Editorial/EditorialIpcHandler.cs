using System.Text.Json;
using Microsoft.Extensions.Logging;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;

namespace TravelCut.Application.Editorial;

public sealed class EditorialIpcHandler
{
    private readonly ISceneClassificationService _sceneService;
    private readonly ITransitionRecommender _transitionRecommender;
    private readonly IEditorialEffectsService _effectsService;
    private readonly ISmartZoomRecommender _zoomRecommender;
    private readonly ILogger<EditorialIpcHandler> _logger;

    public EditorialIpcHandler(
        ISceneClassificationService sceneService,
        ITransitionRecommender transitionRecommender,
        IEditorialEffectsService effectsService,
        ISmartZoomRecommender zoomRecommender,
        ILogger<EditorialIpcHandler> logger)
    {
        _sceneService = sceneService;
        _transitionRecommender = transitionRecommender;
        _effectsService = effectsService;
        _zoomRecommender = zoomRecommender;
        _logger = logger;
    }

    public async Task<IpcResponse?> TryHandleAsync(IpcRequest request, CancellationToken ct)
    {
        try
        {
            switch (request.Type)
            {
                case "scene.upsert":
                {
                    var req = Deserialize<UpsertSceneClassificationRequest>(request.Payload);
                    if (req == null) return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Payload de classificacao de cena invalido.");
                    var res = await _sceneService.UpsertClassificationAsync(req, ct);
                    return IpcResponse.Success(request.RequestId, new { entry = res });
                }

                case "scene.batchClassify":
                {
                    var req = Deserialize<BatchClassifyRequest>(request.Payload);
                    if (req == null) return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Payload de lote de classificacao invalido.");
                    var res = await _sceneService.BatchClassifyAsync(req, ct);
                    return IpcResponse.Success(request.RequestId, new { entries = res });
                }

                case "scene.delete":
                {
                    var idStr = GetStringProperty(request.Payload, "entryId");
                    if (!Guid.TryParse(idStr, out var entryId))
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "entryId invalido.");
                    var deleted = await _sceneService.DeleteClassificationAsync(entryId, ct);
                    return IpcResponse.Success(request.RequestId, new { deleted });
                }

                case "scene.listByProject":
                {
                    var idStr = GetStringProperty(request.Payload, "projectId");
                    if (!Guid.TryParse(idStr, out var projectId))
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "projectId invalido.");
                    var res = await _sceneService.GetByProjectAsync(projectId, ct);
                    return IpcResponse.Success(request.RequestId, new { classifications = res });
                }

                case "scene.listByMedia":
                {
                    var idStr = GetStringProperty(request.Payload, "mediaId");
                    if (!Guid.TryParse(idStr, out var mediaId))
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "mediaId invalido.");
                    var res = await _sceneService.GetByMediaAsync(mediaId, ct);
                    return IpcResponse.Success(request.RequestId, new { classifications = res });
                }

                case "scene.getEffective":
                {
                    var idStr = GetStringProperty(request.Payload, "mediaId");
                    if (!Guid.TryParse(idStr, out var mediaId))
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "mediaId invalido.");
                    var time = GetDoubleProperty(request.Payload, "timestampSeconds") ?? 0.0;
                    var res = await _sceneService.GetEffectiveClassificationAtAsync(mediaId, time, ct);
                    return IpcResponse.Success(request.RequestId, new { classification = res });
                }

                case "transition.importSuggestions":
                {
                    var idStr = GetStringProperty(request.Payload, "projectId");
                    if (!Guid.TryParse(idStr, out var projectId))
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "projectId invalido.");
                    var profile = GetStringProperty(request.Payload, "editorialProfile") ?? "natural";
                    var list = await _transitionRecommender.GenerateImportSuggestionsAsync(projectId, profile, ct);
                    return IpcResponse.Success(request.RequestId, new { suggestions = list });
                }

                case "transition.createManual":
                {
                    var req = Deserialize<CreateTransitionRequest>(request.Payload);
                    if (req == null) return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Payload de transicao manual invalido.");
                    var res = await _effectsService.CreateManualTransitionAsync(req, ct);
                    return IpcResponse.Success(request.RequestId, new { transition = res });
                }

                case "transition.updateStatus":
                {
                    var idStr = GetStringProperty(request.Payload, "transitionId");
                    if (!Guid.TryParse(idStr, out var transitionId))
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "transitionId invalido.");
                    var status = GetStringProperty(request.Payload, "status") ?? "pending";
                    var res = await _effectsService.UpdateTransitionStatusAsync(transitionId, status, ct);
                    if (res == null) return IpcResponse.Fail(request.RequestId, "NOT_FOUND", "Transicao nao encontrada.");
                    return IpcResponse.Success(request.RequestId, new { transition = res });
                }

                case "transition.delete":
                {
                    var idStr = GetStringProperty(request.Payload, "transitionId");
                    if (!Guid.TryParse(idStr, out var transitionId))
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "transitionId invalido.");
                    var deleted = await _effectsService.DeleteTransitionAsync(transitionId, ct);
                    return IpcResponse.Success(request.RequestId, new { deleted });
                }

                case "transition.listByProject":
                {
                    var idStr = GetStringProperty(request.Payload, "projectId");
                    if (!Guid.TryParse(idStr, out var projectId))
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "projectId invalido.");
                    var list = await _effectsService.GetByProjectAsync(projectId, ct);
                    return IpcResponse.Success(request.RequestId, new { transitions = list });
                }

                case "transition.validateHandles":
                {
                    var req = Deserialize<ValidateHandlesRequest>(request.Payload);
                    if (req == null) return IpcResponse.Fail(request.RequestId, "INVALID_PAYLOAD", "Payload de validacao de handles invalido.");
                    var res = await _effectsService.ValidateHandlesAsync(req, ct);
                    return IpcResponse.Success(request.RequestId, new { validation = res });
                }

                case "transition.validateAdjacency":
                {
                    var idStr = GetStringProperty(request.Payload, "transitionId");
                    if (!Guid.TryParse(idStr, out var transitionId))
                        return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "transitionId invalido.");
                    var isAdjacent = await _transitionRecommender.ValidateAdjacencyOnTimelineAsync(transitionId, ct);
                    return IpcResponse.Success(request.RequestId, new { isAdjacent });
                }

                case "effects.previewStates":
                {
                    var idStr = GetStringProperty(request.Payload, "projectId");
                    Guid.TryParse(idStr, out var projectId);
                    var states = _effectsService.GetPreviewStates(projectId);
                    return IpcResponse.Success(request.RequestId, new { previewStates = states });
                }

                case "zoom.getProfile":
                {
                    var interaction = GetStringProperty(request.Payload, "interaction") ?? "direct_to_camera";
                    var profile = _zoomRecommender.GetProfileForInteraction(interaction);
                    return IpcResponse.Success(request.RequestId, new { profile });
                }

                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar comando editorial: {Type}", request.Type);
            return IpcResponse.Fail(request.RequestId, "INTERNAL_ERROR", ex.Message);
        }
    }

    private static T? Deserialize<T>(object? payload) where T : class
    {
        if (payload == null) return null;
        if (payload is JsonElement elem)
        {
            var json = elem.GetRawText();
            return JsonSerializer.Deserialize<T>(json);
        }
        var s = JsonSerializer.Serialize(payload);
        return JsonSerializer.Deserialize<T>(s);
    }

    private static string? GetStringProperty(object? payload, string propertyName)
    {
        if (payload is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out var prop))
                return prop.GetString();
        }
        return null;
    }

    private static double? GetDoubleProperty(object? payload, string propertyName)
    {
        if (payload is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.TryGetDouble(out var d))
                return d;
        }
        return null;
    }
}
