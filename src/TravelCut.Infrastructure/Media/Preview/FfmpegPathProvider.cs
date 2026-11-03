using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Media.Thumbnails;

namespace TravelCut.Infrastructure.Media.Preview;

/// <summary>
/// Localização do FFmpeg para a prévia (mesma regra de <see cref="FfmpegLocator"/>: variável de ambiente,
/// locais comuns e PATH). Resultado em cache por processo: instalar o FFmpeg exige reiniciar o app.
/// </summary>
public sealed class FfmpegPathProvider : IFfmpegPathProvider
{
    private readonly Lazy<string?> _path;

    public FfmpegPathProvider()
        : this(FfmpegLocator.Locate)
    {
    }

    /// <summary>Construtor para testes.</summary>
    public FfmpegPathProvider(Func<string?> locate)
    {
        _path = new Lazy<string?>(locate, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string? GetFfmpegPath() => _path.Value;
}
