using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Audio;
using TravelCut.Application.Ipc;
using TravelCut.Application.Services;
using TravelCut.Contracts.Ipc;
using TravelCut.Infrastructure.Composition;
using TravelCut.Infrastructure.Persistence;
using TravelCut.ResolveBridge;
using Xunit;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>
/// App montado pela composição real (<c>AddTravelCutCore</c>) sobre um SQLite temporário, com o editor em modo mock
/// (equivale a <c>TRAVELCUT_EDITOR_ADAPTER=mock</c>, via <see cref="TravelCutHostOptions.UseMockEditor"/>).
/// Toda ação passa pelo <see cref="IpcDispatcher"/>, em escopo próprio, como faz a UI.
/// As únicas substituições são as três portas de mídia de <see cref="E2EPortFixtures"/>.
/// </summary>
public sealed class E2EHost : IDisposable
{
    private readonly ServiceProvider _provider;

    private E2EHost(ServiceProvider provider)
    {
        _provider = provider;
    }

    /// <summary>
    /// Sobe o app sobre <paramref name="databasePath"/>. Chama <c>EnsureSchemaUpdated</c> como o Desktop faz na inicialização.
    /// Trocar o host por outro sobre o mesmo banco simula reinício do app.
    /// </summary>
    public static E2EHost Start(string databasePath, E2EPortFixtures fixtures)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddTravelCutCore(new TravelCutHostOptions
        {
            DatabasePath = databasePath,
            AppBaseDirectory = AppContext.BaseDirectory,
            UseMockEditor = true,
        });

        // Fixtures de porta (somente estas três): ffprobe, Whisper e silencedetect.
        services.Replace(ServiceDescriptor.Singleton<IFfprobeExtractor>(fixtures));
        services.Replace(ServiceDescriptor.Singleton<IWhisperTranscriptionService>(fixtures));
        services.Replace(ServiceDescriptor.Singleton<ISilenceDetectionService>(fixtures));

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TravelCutDbContext>().EnsureSchemaUpdated();
        }

        return new E2EHost(provider);
    }

    /// <summary>Envia uma requisição IPC e devolve a resposta no formato JSON que a UI recebe.</summary>
    public async Task<JsonElement> Send(string type, object? payload = null, CancellationToken ct = default)
    {
        using var scope = _provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IpcDispatcher>();
        var request = new IpcRequest
        {
            Type = type,
            Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload),
        };
        var response = await dispatcher.DispatchAsync(request, ct);
        return JsonSerializer.SerializeToElement(response);
    }

    /// <summary>Espera resposta ok e devolve o payload.</summary>
    public async Task<JsonElement> Ok(string type, object? payload = null, CancellationToken ct = default)
    {
        var wire = await Send(type, payload, ct);
        Assert.True(wire.GetProperty("ok").GetBoolean(), $"{type} deveria ter sido bem-sucedido: {wire.GetRawText()}");
        return wire.GetProperty("payload").Clone();
    }

    /// <summary>Espera falha e devolve o código de erro IPC.</summary>
    public async Task<string> Fail(string type, object? payload = null, CancellationToken ct = default)
    {
        var wire = await Send(type, payload, ct);
        Assert.False(wire.GetProperty("ok").GetBoolean(), $"{type} deveria ter falhado: {wire.GetRawText()}");
        return wire.GetProperty("error").GetProperty("code").GetString()!;
    }

    public void Dispose() => _provider.Dispose();
}
