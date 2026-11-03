using TravelCut.Contracts.Dtos;

namespace TravelCut.Application.Motion;

/// <summary>
/// Contexto já confinado de um render: todos os caminhos são absolutos e derivados de <see cref="MotionPaths"/>.
/// <c>OutputDirAbsolute</c> é sempre <c>&lt;projeto&gt;/3-Trabalho/motion/&lt;renderId&gt;</c>; <c>AssetsRoot</c> é
/// <c>&lt;projeto&gt;/2-Assets</c> (somente leitura). Caminhos absolutos nunca saem para a UI ou para mensagens.
/// </summary>
public sealed record MotionRenderContext(string ProjectRoot, string RenderId, string OutputDirAbsolute, string AssetsRoot)
{
    public string ManifestAbsolutePath => Path.Combine(OutputDirAbsolute, MotionPaths.RequestFileName);
    public string ResultAbsolutePath => Path.Combine(OutputDirAbsolute, MotionPaths.ResultFileName);
    public string OutputFileAbsolute => Path.Combine(OutputDirAbsolute, MotionPaths.OutputFileName);
}

/// <summary>
/// Porta do renderizador de animações (F09.5). Implementações: <c>MockMotionRenderer</c> (SIMULATED, sem vídeo) e
/// <c>HyperFramesRenderer</c> (Node + CLI local, estado real declarado). Não há fallback silencioso entre elas.
/// </summary>
public interface IMotionRenderer
{
    /// <summary>"mock" ou "hyperframes".</summary>
    string Mode { get; }

    Task<MotionCapabilitiesDto> DetectCapabilitiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Executa o render de um pedido já validado. O manifesto de entrada (request.json) já existe em
    /// <see cref="MotionRenderContext.ManifestAbsolutePath"/>. Só devolve READY_REAL com arquivo de saída existente.
    /// </summary>
    Task<MotionRenderResultDto> RenderAsync(MotionRenderRequestDto request, MotionRenderContext context, CancellationToken ct = default);
}
