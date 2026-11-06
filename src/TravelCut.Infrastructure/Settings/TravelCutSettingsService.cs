using System.Text.Json;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;

namespace TravelCut.Infrastructure.Settings;

public sealed class TravelCutSettingsService : ITravelCutSettingsService
{
    private readonly string _settingsFilePath;
    private readonly ILogger<TravelCutSettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TravelCutSettingsData? _cache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public TravelCutSettingsService(
        ILogger<TravelCutSettingsService> logger,
        string? customSettingsPath = null)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(customSettingsPath))
        {
            _settingsFilePath = customSettingsPath;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folder = Path.Combine(localAppData, "TravelCutStudio");
            Directory.CreateDirectory(folder);
            _settingsFilePath = Path.Combine(folder, "settings.json");
        }
    }

    public async Task<TravelCutSettingsData> GetSettingsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cache != null) return _cache;

            if (!File.Exists(_settingsFilePath))
            {
                _cache = new TravelCutSettingsData();
                return _cache;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_settingsFilePath, ct).ConfigureAwait(false);
                var data = JsonSerializer.Deserialize<TravelCutSettingsData>(json, JsonOptions);
                _cache = data ?? new TravelCutSettingsData();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao ler arquivo de configurações {Path}; usando valores padrão.", _settingsFilePath);
                _cache = new TravelCutSettingsData();
            }

            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetToolPathAsync(string toolKey, string path, CancellationToken ct = default)
    {
        var normalizedKey = toolKey.Trim().ToLowerInvariant();
        if (normalizedKey is not ("python" or "node" or "nodejs" or "hyperframes" or "ffmpeg"))
        {
            throw new ArgumentException($"Chave de ferramenta inválida '{toolKey}'.");
        }

        var trimmed = path?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("O caminho da ferramenta não pode ser vazio.");
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("O caminho configurado contém caracteres inválidos.");
        }

        if (!Path.IsPathRooted(trimmed))
        {
            throw new ArgumentException("O caminho configurado deve ser um caminho absoluto.");
        }

        if (trimmed.Contains(".."))
        {
            throw new ArgumentException("O caminho configurado não pode conter sequências de travessia ('..').");
        }

        

        var fileName = Path.GetFileName(trimmed).ToLowerInvariant();
        var ext = Path.GetExtension(trimmed).ToLowerInvariant();

        switch (normalizedKey)
        {
            case "python":
                if (ext != ".exe" || (!fileName.Contains("python") && !fileName.Contains("py")))
                {
                    throw new ArgumentException($"Caminho inválido para Python: '{fileName}' não é um executável Python válido.");
                }
                break;

            case "node" or "nodejs":
                if (ext != ".exe" || !fileName.Contains("node"))
                {
                    throw new ArgumentException($"Caminho inválido para Node.js: '{fileName}' não é 'node.exe'.");
                }
                break;

            case "ffmpeg":
                if (ext != ".exe" || (!fileName.Contains("ffmpeg") && !fileName.Contains("ffprobe")))
                {
                    throw new ArgumentException($"Caminho inválido para FFmpeg: '{fileName}' não é 'ffmpeg.exe' nem 'ffprobe.exe'.");
                }
                break;

            case "hyperframes":
                if (ext is not (".exe" or ".js") || (!fileName.StartsWith("hyperframes") && fileName != "cli.js"))
                {
                    throw new ArgumentException($"Caminho inválido para HyperFrames: '{fileName}' não é um executável ou script HyperFrames válido.");
                }
                break;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = _cache ?? await ReadInternalAsync(ct).ConfigureAwait(false);
            var updated = normalizedKey switch
            {
                "python" => current with { PythonPath = trimmed },
                "ffmpeg" => current with { FfmpegPath = trimmed },
                "node" or "nodejs" => current with { NodePath = trimmed },
                "hyperframes" => current with { HyperFramesCliPath = trimmed },
                _ => current
            };

            await SaveInternalAsync(updated, ct).ConfigureAwait(false);
            _cache = updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> GetToolPathAsync(string toolKey, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct).ConfigureAwait(false);
        return toolKey.Trim().ToLowerInvariant() switch
        {
            "python" => settings.PythonPath,
            "node" or "nodejs" => settings.NodePath,
            "hyperframes" => settings.HyperFramesCliPath,
            "ffmpeg" => settings.FfmpegPath,
            _ => null
        };
    }

    public async Task SetMediaSourceFoldersAsync(IEnumerable<string> folders, CancellationToken ct = default)
    {
        var validFolders = new List<string>();
        foreach (var f in folders)
        {
            var t = f?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(t) && !t.Any(char.IsControl) && Path.IsPathRooted(t) && !t.Contains(".."))
            {
                validFolders.Add(t);
            }
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = _cache ?? await ReadInternalAsync(ct).ConfigureAwait(false);
            var updated = current with { MediaSourceFolders = validFolders.Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
            await SaveInternalAsync(updated, ct).ConfigureAwait(false);
            _cache = updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddMediaSourceFolderAsync(string folder, CancellationToken ct = default)
    {
        var trimmed = folder?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Any(char.IsControl) || !Path.IsPathRooted(trimmed) || trimmed.Contains(".."))
        {
            throw new ArgumentException("Pasta de mídia inválida.", nameof(folder));
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = _cache ?? await ReadInternalAsync(ct).ConfigureAwait(false);
            var list = new List<string>(current.MediaSourceFolders);
            if (!list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(trimmed);
                var updated = current with { MediaSourceFolders = list };
                await SaveInternalAsync(updated, ct).ConfigureAwait(false);
                _cache = updated;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveMediaSourceFolderAsync(string folder, CancellationToken ct = default)
    {
        var trimmed = folder?.Trim() ?? string.Empty;
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = _cache ?? await ReadInternalAsync(ct).ConfigureAwait(false);
            var list = new List<string>(current.MediaSourceFolders);
            if (list.RemoveAll(f => string.Equals(f, trimmed, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                var updated = current with { MediaSourceFolders = list };
                await SaveInternalAsync(updated, ct).ConfigureAwait(false);
                _cache = updated;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<TravelCutSettingsData> ReadInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_settingsFilePath))
            return new TravelCutSettingsData();

        try
        {
            var json = await File.ReadAllTextAsync(_settingsFilePath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<TravelCutSettingsData>(json, JsonOptions) ?? new TravelCutSettingsData();
        }
        catch
        {
            return new TravelCutSettingsData();
        }
    }

    private async Task SaveInternalAsync(TravelCutSettingsData data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var dir = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = _settingsFilePath + ".tmp";
        await File.WriteAllTextAsync(tempPath, json, ct).ConfigureAwait(false);
        File.Move(tempPath, _settingsFilePath, overwrite: true);
    }
}
