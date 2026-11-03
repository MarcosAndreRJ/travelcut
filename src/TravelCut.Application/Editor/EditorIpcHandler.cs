using System.Text.Json;
using TravelCut.Application.Planning;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;

namespace TravelCut.Application.Editor;

/// <summary>
/// Trata os métodos IPC do editor (F08): editor.capabilities, editor.prepareApply, editor.apply, editor.applications.
/// Para outros tipos devolve null, para o IpcDispatcher continuar o roteamento. Escopo: scoped.
/// </summary>
public sealed class EditorIpcHandler
{
    public const string CapabilitiesType = "editor.capabilities";
    public const string PrepareApplyType = "editor.prepareApply";
    public const string ApplyType = "editor.apply";
    public const string ApplicationsType = "editor.applications";

    private readonly EditorUseCases _useCases;

    public EditorIpcHandler(EditorUseCases useCases)
    {
        _useCases = useCases;
    }

    public async Task<IpcResponse?> TryHandleAsync(IpcRequest request, CancellationToken ct = default)
    {
        switch (request.Type)
        {
            case CapabilitiesType:
            {
                var caps = await _useCases.CapabilitiesAsync(ct);
                return IpcResponse.Success(request.RequestId, caps);
            }

            case PrepareApplyType:
            {
                var req = DeserializePayload<PrepareEditorApplyRequest>(request.Payload);
                if (req is null || req.ProjectId == Guid.Empty)
                    return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "ID de projeto inválido.");

                var outcome = await _useCases.PrepareApplyAsync(req, ct);
                return ToResponse(request.RequestId, outcome);
            }

            case ApplyType:
            {
                var req = DeserializePayload<ApplyEditorPlanRequest>(request.Payload);
                if (req is null || req.ProjectId == Guid.Empty)
                    return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "ID de projeto inválido.");

                var outcome = await _useCases.ApplyAsync(req, ct);
                return ToResponse(request.RequestId, outcome);
            }

            case ApplicationsType:
            {
                var req = DeserializePayload<ListEditorApplicationsRequest>(request.Payload);
                if (req is null || req.ProjectId == Guid.Empty)
                    return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "ID de projeto inválido.");

                var outcome = await _useCases.ListApplicationsAsync(req.ProjectId, ct);
                return ToResponse(request.RequestId, outcome);
            }

            default:
                return null;
        }
    }

    private static IpcResponse ToResponse<T>(string requestId, PlanOutcome<T> outcome) =>
        outcome.Success
            ? IpcResponse.Success(requestId, outcome.Value)
            : IpcResponse.Fail(requestId, outcome.ErrorCode ?? "EDITOR_FAILED", outcome.ErrorMessage ?? "Falha no editor.");

    // Mesmo padrão do PreviewIpcHandler (cópia local: cada handler é independente do IpcDispatcher).
    private static T? DeserializePayload<T>(object? payload)
    {
        if (payload is null) return default;
        if (payload is T direct) return direct;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        if (payload is JsonElement json)
        {
            return JsonSerializer.Deserialize<T>(json.GetRawText(), options);
        }

        string raw = JsonSerializer.Serialize(payload);
        return JsonSerializer.Deserialize<T>(raw, options);
    }
}
