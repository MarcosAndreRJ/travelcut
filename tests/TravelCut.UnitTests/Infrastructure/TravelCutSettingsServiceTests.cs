using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Infrastructure.Settings;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

public class TravelCutSettingsServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _settingsFilePath;

    public TravelCutSettingsServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "tc_settings_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _settingsFilePath = Path.Combine(_tempDirectory, "settings.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task GetSettingsAsync_WhenFileDoesNotExist_ReturnsDefaultsAndCreatesDirectory()
    {
        var service = new TravelCutSettingsService(NullLogger<TravelCutSettingsService>.Instance, _settingsFilePath);

        var settings = await service.GetSettingsAsync();

        Assert.NotNull(settings);
        Assert.Null(settings.PythonPath);
        Assert.Null(settings.NodePath);
        Assert.Null(settings.HyperFramesCliPath);
        Assert.Null(settings.FfmpegPath);
        Assert.Empty(settings.MediaSourceFolders);
    }

    [Fact]
    public async Task SetToolPathAsync_PersistsToolPathAndReloads()
    {
        var service = new TravelCutSettingsService(NullLogger<TravelCutSettingsService>.Instance, _settingsFilePath);

        await service.SetToolPathAsync("python", "C:\\Python311\\python.exe");
        var path = await service.GetToolPathAsync("python");

        Assert.Equal("C:\\Python311\\python.exe", path);

        // Instancia novo serviço apontando para o mesmo arquivo para verificar persistência em disco
        var reloadedService = new TravelCutSettingsService(NullLogger<TravelCutSettingsService>.Instance, _settingsFilePath);
        var reloadedPath = await reloadedService.GetToolPathAsync("python");
        Assert.Equal("C:\\Python311\\python.exe", reloadedPath);
    }

    [Fact]
    public async Task SetMediaSourceFoldersAsync_PersistsAndDeduplicates()
    {
        var service = new TravelCutSettingsService(NullLogger<TravelCutSettingsService>.Instance, _settingsFilePath);

        await service.AddMediaSourceFolderAsync("D:\\Videos\\Gopro");
        await service.AddMediaSourceFolderAsync("D:\\Videos\\Drone");
        // Adiciona duplicado
        await service.AddMediaSourceFolderAsync("D:\\Videos\\Gopro");

        var settings = await service.GetSettingsAsync();
        Assert.Equal(2, settings.MediaSourceFolders.Count);
        Assert.Contains("D:\\Videos\\Gopro", settings.MediaSourceFolders);
        Assert.Contains("D:\\Videos\\Drone", settings.MediaSourceFolders);

        await service.RemoveMediaSourceFolderAsync("D:\\Videos\\Gopro");
        var updated = await service.GetSettingsAsync();
        Assert.Single(updated.MediaSourceFolders);
        Assert.Equal("D:\\Videos\\Drone", updated.MediaSourceFolders[0]);
    }

    [Fact]
    public async Task CorruptedFile_FallsBackToDefaultsGracefully()
    {
        await File.WriteAllTextAsync(_settingsFilePath, "invalid json {{{ corrupted");

        var service = new TravelCutSettingsService(NullLogger<TravelCutSettingsService>.Instance, _settingsFilePath);
        var settings = await service.GetSettingsAsync();

        Assert.NotNull(settings);
        Assert.Empty(settings.MediaSourceFolders);
    }

    [Fact]
    public async Task SetToolPathAsync_WithSpacesAndUnicodeAndDifferentDrive_PersistsCorrectly()
    {
        var service1 = new TravelCutSettingsService(NullLogger<TravelCutSettingsService>.Instance, _settingsFilePath);
        const string customPython = @"E:\Ferramentas Edição\Python 3.11\python.exe";
        const string customNode = @"D:\Ambiente de Vídeo\NodeJS\node.exe";
        const string unicodeSource = @"F:\Gravações Viagem - São Paulo & Ação\Brutos";

        await service1.SetToolPathAsync("python", customPython);
        await service1.SetToolPathAsync("node", customNode);
        await service1.AddMediaSourceFolderAsync(unicodeSource);

        // Recria a instância do serviço (simulando reinício do aplicativo)
        var service2 = new TravelCutSettingsService(NullLogger<TravelCutSettingsService>.Instance, _settingsFilePath);
        var settings = await service2.GetSettingsAsync();

        Assert.Equal(customPython, settings.PythonPath);
        Assert.Equal(customNode, settings.NodePath);
        Assert.Contains(unicodeSource, settings.MediaSourceFolders);

        // Garante que nenhum arquivo temporário (.tmp.*) permaneceu na pasta
        var tmpFiles = Directory.GetFiles(_tempDirectory, "*.tmp.*");
        Assert.Empty(tmpFiles);
    }
}
