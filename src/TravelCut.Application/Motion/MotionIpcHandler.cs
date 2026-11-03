using TravelCut.Contracts.Ipc;

namespace TravelCut.Application.Motion;

/// <summary>
/// Trata os métodos IPC de animações (F09): por ora só motion.capabilities (integração HyperFrames declarada por capability).
/// Render via IPC fica para quando houver tela que o use. Para outros tipos devolve null.
/// </summary>
public sealed class MotionIpcHandler
{
    public const string CapabilitiesType = "motion.capabilities";

    private readonly MotionUseCases _useCases;

    public MotionIpcHandler(MotionUseCases useCases)
    {
        _useCases = useCases;
    }

    public async Task<IpcResponse?> TryHandleAsync(IpcRequest request, CancellationToken ct = default)
    {
        if (request.Type != CapabilitiesType) return null;
        var caps = await _useCases.DetectCapabilitiesAsync(ct);
        return IpcResponse.Success(request.RequestId, caps);
    }
}
