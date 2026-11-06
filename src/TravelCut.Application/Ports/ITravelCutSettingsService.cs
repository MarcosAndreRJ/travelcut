namespace TravelCut.Application.Ports;

public sealed record TravelCutSettingsData
{
    public string? PythonPath { get; init; }
    public string? NodePath { get; init; }
    public string? HyperFramesCliPath { get; init; }
    public string? FfmpegPath { get; init; }
    public string? PreferredWhisperDevice { get; init; }
    public List<string> MediaSourceFolders { get; init; } = [];
}

public interface ITravelCutSettingsService
{
    Task<TravelCutSettingsData> GetSettingsAsync(CancellationToken ct = default);
    Task SetToolPathAsync(string toolKey, string path, CancellationToken ct = default);
    Task<string?> GetToolPathAsync(string toolKey, CancellationToken ct = default);
    Task SetMediaSourceFoldersAsync(IEnumerable<string> folders, CancellationToken ct = default);
    Task AddMediaSourceFolderAsync(string folder, CancellationToken ct = default);
    Task RemoveMediaSourceFolderAsync(string folder, CancellationToken ct = default);
}
