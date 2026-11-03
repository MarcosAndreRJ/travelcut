using TravelCut.Application.Motion;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Motion;

/// <summary>
/// Simulador de animações (F09.5), sem Node e sem HyperFrames. Sempre isSimulated=true, resultado SIMULATED
/// e outputRelativePath=null: nunca cria arquivo de vídeo. Não é apresentado como render real.
/// </summary>
public sealed class MockMotionRenderer : IMotionRenderer
{
    public const string ModeName = "mock";
    public const string StatusMessage = "Modo simulado: nenhum CLI do HyperFrames é executado; os resultados são SIMULATED.";
    public const string NoRenderMessage = "DEMONSTRAÇÃO: nenhum vídeo foi renderizado.";
    public const string TemplateNote = "Template demonstrativo no simulador; sem render real.";

    private readonly TimeProvider _time;

    public MockMotionRenderer(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    public string Mode => ModeName;

    public Task<MotionCapabilitiesDto> DetectCapabilitiesAsync(CancellationToken ct = default)
    {
        var caps = new MotionCapabilitiesDto(
            SchemaVersion: MotionSchemaVersions.Capabilities,
            AdapterMode: ModeName,
            EnvironmentState: MotionEnvironmentStates.Mock,
            IsAvailable: true,
            IsSimulated: true,
            NodeVersion: null,
            RendererVersion: null,
            StatusMessage: StatusMessage,
            ReasonCode: null,
            Templates: MotionCapabilityTemplates.Build(supported: true, CapabilityVerification.MockPass, TemplateNote),
            CheckedAtUtc: _time.GetUtcNow().UtcDateTime);
        return Task.FromResult(caps);
    }

    public Task<MotionRenderResultDto> RenderAsync(MotionRenderRequestDto request, MotionRenderContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        ct.ThrowIfCancellationRequested();
        DateTime now = _time.GetUtcNow().UtcDateTime;

        // Nenhum arquivo de saída é criado; OutputRelativePath permanece nulo por contrato.
        var result = new MotionRenderResultDto(
            Status: MotionRenderStatuses.Simulated,
            IsSimulated: true,
            OutputRelativePath: null,
            ManifestRelativePath: MotionPaths.ManifestRelative(context.RenderId),
            ErrorCode: null,
            ErrorMessage: NoRenderMessage,
            StartedAtUtc: now,
            FinishedAtUtc: now);
        return Task.FromResult(result);
    }
}
