using System.Text.Json;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Media.Preview;

/// <summary>
/// Cache de formas de onda em &lt;projeto&gt;/3-Trabalho/cache/waveforms/&lt;chave&gt;.json.
/// Envelope versionado (schemaVersion). Só resultados "ok" são gravados. Leitura inválida = cache miss.
/// Escrita: arquivo temporário e rename sem sobrescrita.
/// </summary>
public sealed class FileWaveformCache : IWaveformCache
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<WaveformDto?> ReadAsync(string projectRoot, string cacheKey, CancellationToken ct = default)
    {
        string path = PathFor(projectRoot, cacheKey);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            var envelope = await JsonSerializer.DeserializeAsync<Envelope>(stream, JsonOptions, ct);
            if (envelope == null || envelope.SchemaVersion != SchemaVersion || envelope.Result == null)
                return null;

            var r = envelope.Result;
            // Validação mínima do conteúdo antes de servir do cache.
            if (r.Status != "ok" || r.Peaks == null || r.Peaks.Count == 0 || r.Peaks.Any(p => !double.IsFinite(p) || p < 0 || p > 1))
                return null;

            return r;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Arquivo ilegível ou bloqueado: cache miss (a forma de onda é recalculada).
            return null;
        }
    }

    public async Task WriteAsync(string projectRoot, string cacheKey, WaveformDto result, CancellationToken ct = default)
    {
        if (result.Status != "ok")
            return;

        string path = PathFor(projectRoot, cacheKey);
        string dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        if (File.Exists(path))
            return;

        string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, new Envelope(SchemaVersion, result), cancellationToken: ct);
            }

            if (!File.Exists(path))
            {
                File.Move(tempPath, path, overwrite: false);
            }
        }
        finally
        {
            // Limpeza best-effort: não pode substituir a exceção original da gravação (IO ou acesso negado).
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string PathFor(string projectRoot, string cacheKey)
    {
        if (string.IsNullOrWhiteSpace(cacheKey) || cacheKey.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Chave de cache inválida.", nameof(cacheKey));

        return Path.Combine(
            Path.GetFullPath(projectRoot),
            PreviewPaths.WaveformFolderRelative.Replace('/', Path.DirectorySeparatorChar),
            cacheKey + ".json");
    }

    private sealed record Envelope(int SchemaVersion, WaveformDto? Result);
}
