using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Infrastructure.Audio;
using TravelCut.Infrastructure.Media.Preview;

namespace TravelCut.Desktop;

/// <summary>
/// Registro da prévia de mídia (F07). Dependências esperadas no contêiner: IProjectRepository e IMediaRepository
/// (scoped), ILogger. IProcessRunner é registrado aqui apenas se ainda não existir (TryAdd).
/// Serviços com estado (fila de proxies, runner binário) são singletons; casos de uso e handler são scoped.
/// </summary>
public static class PreviewServiceRegistration
{
    public static IServiceCollection AddTravelCutPreview(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IFfmpegPathProvider, FfmpegPathProvider>();
        services.AddSingleton<IBinaryStdoutProcessRunner, BinaryStdoutProcessRunner>();
        services.AddSingleton<IWaveformCache, FileWaveformCache>();

        // Singleton: guarda o estado dos jobs de proxy e encerra o FFmpeg no Dispose do contêiner.
        services.AddSingleton<IProxyJobService, ProxyJobService>();

        services.AddScoped<PreviewUseCases>();
        services.AddScoped<PreviewIpcHandler>();

        return services;
    }
}
