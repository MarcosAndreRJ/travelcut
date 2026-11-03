using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Ipc;
using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Composition;
using TravelCut.Infrastructure.Persistence;
using TravelCut.ResolveBridge;
using Xunit;

namespace TravelCut.Mcp.Tests;

/// <summary>
/// Grafo de DI do Application Core (AddTravelCutCore) validado com ValidateOnBuild e ValidateScopes,
/// como o host do servidor MCP e o Desktop o consomem.
/// </summary>
public sealed class CompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tc_comp_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Melhor esforço.
        }
    }

    private TravelCutHostOptions Options(bool mock) => new()
    {
        DatabasePath = Path.Combine(_root, "db", "travelcut.db"),
        AppBaseDirectory = AppContext.BaseDirectory,
        UseMockEditor = mock,
    };

    private static ServiceProvider Build(IServiceCollection services) => services.BuildServiceProvider(
        new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

    [Fact]
    public void AddTravelCutCore_GrafoValidaEmBuildEEscopos_ComAdaptadorMock()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTravelCutCore(Options(mock: true));

        using var provider = Build(services);
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IpcDispatcher>());
        Assert.IsType<MockResolveAdapter>(provider.GetRequiredService<IEditorAdapter>());
    }

    [Fact]
    public void AddTravelCutCore_SemModoMock_SeleccionaDaVinciSemFallbackAoMock()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTravelCutCore(Options(mock: false));

        using var provider = Build(services);

        Assert.IsType<DaVinciResolveAdapter>(provider.GetRequiredService<IEditorAdapter>());
    }

    [Fact]
    public void AddTravelCutCore_CriaDiretorioDoBancoEAplicaSchema()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTravelCutCore(Options(mock: true));

        using var provider = Build(services);
        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TravelCutDbContext>().EnsureSchemaUpdated();
        }

        Assert.True(File.Exists(Path.Combine(_root, "db", "travelcut.db")));
    }

    [Fact]
    public void AddTravelCutCore_OpcoesInvalidas_LancaArgumentException()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddTravelCutCore(new TravelCutHostOptions
        {
            DatabasePath = " ",
            AppBaseDirectory = AppContext.BaseDirectory,
        }));
    }

    [Fact]
    public void DefaultDatabasePath_ApontaParaPastaDoUsuarioDoTravelCut()
    {
        var path = TravelCutHostOptions.DefaultDatabasePath();

        Assert.EndsWith(Path.Combine("TravelCutStudio", "travelcut.db"), path);
        Assert.True(Path.IsPathFullyQualified(path));
    }
}
