using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Audio;
using TravelCut.ResolveBridge;
using Xunit;

namespace TravelCut.UnitTests.Editor;

/// <summary>
/// Grafo de resolução do adaptador de editor com ValidateOnBuild. Replica o registro de App.xaml.cs (o WPF não roda
/// nos testes): garante que os construtores resolvem e que o ramo "mock" / ramo padrão devolvem o tipo esperado.
/// </summary>
public class EditorDependencyGraphTests
{
    private static ServiceProvider Build(bool useMock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(ResolveBridgeOptions.FromEnvironment(AppContext.BaseDirectory));
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<MockResolveAdapter>();
        services.AddSingleton<DaVinciResolveAdapter>();
        services.AddSingleton<IEditorAdapter>(sp => useMock
            ? sp.GetRequiredService<MockResolveAdapter>()
            : sp.GetRequiredService<DaVinciResolveAdapter>());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void MockSelection_ResolvesSimulator()
    {
        using var provider = Build(useMock: true);

        var adapter = provider.GetRequiredService<IEditorAdapter>();

        Assert.IsType<MockResolveAdapter>(adapter);
        Assert.Equal("mock", adapter.Mode);
    }

    [Fact]
    public void DefaultSelection_ResolvesDaVinciAdapter_WithoutFallbackToMock()
    {
        using var provider = Build(useMock: false);

        var adapter = provider.GetRequiredService<IEditorAdapter>();

        Assert.IsType<DaVinciResolveAdapter>(adapter);
    }
}
