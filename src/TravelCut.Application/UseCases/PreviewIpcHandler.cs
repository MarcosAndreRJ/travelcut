using System.Text.Json;
using Microsoft.Extensions.Logging;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;

namespace TravelCut.Application.UseCases;

/// <summary>
/// Trata os métodos IPC da prévia (F07): media.getPlaybackSource, media.getWaveform, media.proxy.start,
/// media.proxy.status. Para qualquer outro tipo devolve null, para o IpcDispatcher continuar o roteamento.
/// Escopo: scoped (usa repositórios com DbContext).
/// </summary>
public sealed class PreviewIpcHandler
{
    public const string PlaybackSourceType = "media.getPlaybackSource";
    public const string WaveformType = "media.getWaveform";
    public const string ProxyStartType = "media.proxy.start";
    public const string ProxyStatusType = "media.proxy.status";

    private readonly PreviewUseCases _useCases;
    private readonly ILogger<PreviewIpcHandler> _logger;

    public PreviewIpcHandler(PreviewUseCases useCases, ILogger<PreviewIpcHandler> logger)
    {
        _useCases = useCases;
        _logger = logger;
    }

    public async Task<IpcResponse?> TryHandleAsync(IpcRequest request, CancellationToken ct = default)
    {
        switch (request.Type)
        {
            case PlaybackSourceType:
            {
                var req = DeserializePayload<PlaybackSourceRequest>(request.Payload);
                if (req == null || req.ProjectId == Guid.Empty || req.MediaId == Guid.Empty)
                    return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "projectId e mediaId são obrigatórios.");

                var result = await _useCases.GetPlaybackSourceAsync(req, ct);
                return ToResponse(request.RequestId, result);
            }

            case WaveformType:
            {
                var req = DeserializePayload<WaveformRequest>(request.Payload);
                if (req == null || req.ProjectId == Guid.Empty || req.MediaId == Guid.Empty)
                    return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "projectId e mediaId são obrigatórios.");

                var result = await _useCases.GetWaveformAsync(req, ct);
                return ToResponse(request.RequestId, result);
            }

            case ProxyStartType:
            {
                var req = DeserializePayload<StartProxyRequest>(request.Payload);
                if (req == null || req.ProjectId == Guid.Empty)
                    return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "projectId é obrigatório.");

                var result = await _useCases.StartProxyAsync(req, ct);
                return ToResponse(request.RequestId, result);
            }

            case ProxyStatusType:
            {
                var req = DeserializePayload<ProxyStatusPayload>(request.Payload);
                if (req == null || req.ProjectId == Guid.Empty || req.JobId == Guid.Empty)
                    return IpcResponse.Fail(request.RequestId, "INVALID_ARGUMENT", "projectId e jobId são obrigatórios.");

                var result = _useCases.GetProxyStatus(new ProxyStatusRequest(req.ProjectId, req.JobId));
                return ToResponse(request.RequestId, result);
            }

            default:
                return null;
        }
    }

    private static IpcResponse ToResponse<T>(string requestId, PreviewResult<T> result) =>
        result.Ok && result.Value is not null
            ? IpcResponse.Success(requestId, result.Value)
            : IpcResponse.Fail(requestId, result.ErrorCode ?? "PREVIEW_FAILED", result.ErrorMessage ?? "Falha na prévia.");

    /// <summary>Payload de media.proxy.status: { projectId, jobId }. Ambos obrigatórios; o job deve pertencer ao projeto.</summary>
    private sealed record ProxyStatusPayload(Guid ProjectId, Guid JobId);

    // Mesmo padrão de IpcDispatcher.DeserializePayload (cópia local: o dispatcher é de outro agente).
    private static T? DeserializePayload<T>(object? payload)
    {
        if (payload == null) return default;
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
