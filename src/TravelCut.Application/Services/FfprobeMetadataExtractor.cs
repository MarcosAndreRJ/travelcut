using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TravelCut.Domain.Entities;
using TravelCut.Domain.ValueObjects;

namespace TravelCut.Application.Services;

/// <summary>
/// Resultado da análise de metadados. Quando <see cref="MetadataStatus"/> não é "ok",
/// os campos numéricos são 0 (ou null) — nunca valores estimados.
/// </summary>
public record ExtractedMediaInfo(
    string Codec,
    int Width,
    int Height,
    RationalTimeBase TimeBase,
    double FrameRate,
    double DurationSeconds,
    long DurationTicks,
    int AudioChannels,
    int AudioSampleRate,
    DateTime? CreationTimeUtc,
    string SourceDateEvidence,
    bool IsVariableFrameRate,
    int RotationDegrees,
    string? CameraModel,
    string? GpsLocation,
    string MetadataStatus)
{
    /// <summary>Base time desconhecida (0/0). Usada apenas quando não há metadados confiáveis.</summary>
    public static readonly RationalTimeBase UnknownTimeBase = new(0, 0);

    /// <summary>
    /// Metadados indisponíveis: nenhum valor de vídeo/áudio é inventado. Apenas a data de
    /// modificação do arquivo (rotulada como fallback) é preservada, quando o arquivo existe.
    /// </summary>
    public static ExtractedMediaInfo Unavailable(string filePath, string status)
    {
        DateTime? created = null;
        string evidence = "no_timestamp_found";
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Exists)
            {
                created = fi.LastWriteTimeUtc;
                evidence = "file_system_last_write_fallback";
            }
        }
        catch
        {
            // Sem data quando o caminho não pode ser lido.
        }

        return new ExtractedMediaInfo(
            Codec: "unknown",
            Width: 0,
            Height: 0,
            TimeBase: UnknownTimeBase,
            FrameRate: 0.0,
            DurationSeconds: 0.0,
            DurationTicks: 0,
            AudioChannels: 0,
            AudioSampleRate: 0,
            CreationTimeUtc: created,
            SourceDateEvidence: evidence,
            IsVariableFrameRate: false,
            RotationDegrees: 0,
            CameraModel: null,
            GpsLocation: null,
            MetadataStatus: status);
    }
}

public interface IFfprobeExtractor
{
    Task<ExtractedMediaInfo> ExtractMetadataAsync(string filePath, CancellationToken ct = default);
    bool IsFfprobeAvailable();
}

public class FfprobeMetadataExtractor : IFfprobeExtractor
{
    /// <summary>
    /// Indisponibilidade do ffprobe expira após este intervalo: instalar/configurar o ffprobe enquanto o app roda
    /// não exige reinício. Disponibilidade confirmada permanece em cache.
    /// </summary>
    public static readonly TimeSpan UnavailableCacheTtl = TimeSpan.FromSeconds(30);

    private readonly string _ffprobeExecutable;
    private readonly TimeSpan _timeout;
    private readonly TimeProvider _time;
    private readonly object _availabilityLock = new();
    private bool _hasAvailabilityCache;
    private bool _isAvailable;
    private DateTimeOffset _availabilityCheckedAtUtc;

    /// <param name="ffprobePath">Caminho do executável (preferir absoluto, vindo do localizador). Nulo: nome "ffprobe" pelo PATH.</param>
    public FfprobeMetadataExtractor(string? ffprobePath = null, TimeSpan? timeout = null, TimeProvider? timeProvider = null)
    {
        _ffprobeExecutable = string.IsNullOrWhiteSpace(ffprobePath) ? "ffprobe" : NormalizeExecutable(ffprobePath);
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Caminho com diretório vira absoluto; nome puro (ex.: "ffprobe") fica para busca no PATH.</summary>
    private static string NormalizeExecutable(string path)
    {
        string trimmed = path.Trim();
        bool hasDirectory = Path.IsPathRooted(trimmed) || trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar);
        return hasDirectory ? Path.GetFullPath(trimmed) : trimmed;
    }

    public bool IsFfprobeAvailable()
    {
        DateTimeOffset now = _time.GetUtcNow();
        lock (_availabilityLock)
        {
            if (_hasAvailabilityCache && (_isAvailable || now - _availabilityCheckedAtUtc < UnavailableCacheTtl))
                return _isAvailable;
        }

        bool available = Probe();

        lock (_availabilityLock)
        {
            _isAvailable = available;
            _availabilityCheckedAtUtc = now;
            _hasAvailabilityCache = true;
        }
        return available;
    }

    /// <summary>Executa "ffprobe -version" (3 s). Virtual apenas para testes do cache de disponibilidade.</summary>
    protected virtual bool Probe()
    {
        try
        {
            using var proc = new Process { StartInfo = CreateStartInfo() };
            proc.StartInfo.ArgumentList.Add("-version");

            proc.Start();
            bool exited = proc.WaitForExit(3000);
            if (!exited)
            {
                KillQuietly(proc);
            }
            return exited && proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ExtractedMediaInfo> ExtractMetadataAsync(string filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("Arquivo de mídia não encontrado para extração.", filePath);

        if (!IsFfprobeAvailable())
            return ExtractFallback(filePath, MediaMetadataStatus.FfprobeUnavailable);

        try
        {
            return await ExtractWithProcessAsync(filePath, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Falha/timeout do ffprobe: status explícito, sem valores inventados.
            return ExtractFallback(filePath, MediaMetadataStatus.Failed);
        }
    }

    private async Task<ExtractedMediaInfo> ExtractWithProcessAsync(string filePath, CancellationToken ct)
    {
        using var proc = new Process { StartInfo = CreateStartInfo() };
        // Caminho passado como argumento separado: sem interpolação em shell/linha de comando.
        foreach (var arg in new[] { "-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", "-show_error", filePath })
        {
            proc.StartInfo.ArgumentList.Add(arg);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        proc.Start();
        try
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

            await proc.WaitForExitAsync(cts.Token);

            string stdout = await stdoutTask;
            _ = await stderrTask;

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                throw new InvalidOperationException($"FFprobe falhou com código {proc.ExitCode}.");
            }

            return ParseFfprobeJson(stdout, filePath);
        }
        finally
        {
            if (!proc.HasExited)
            {
                KillQuietly(proc);
            }
        }
    }

    private ProcessStartInfo CreateStartInfo() => new()
    {
        FileName = _ffprobeExecutable,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        // ffprobe emite JSON em UTF-8 (tags com acento); sem isso o Windows decodifica pela página de código OEM.
        StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        UseShellExecute = false,
        CreateNoWindow = true
    };

    private static void KillQuietly(Process proc)
    {
        try
        {
            proc.Kill(entireProcessTree: true);
        }
        catch
        {
            // Processo já encerrado ou sem permissão: nada a fazer.
        }
    }

    public ExtractedMediaInfo ParseFfprobeJson(string json, string filePath)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var format = root.TryGetProperty("format", out var f) ? f : default;
        var streams = root.TryGetProperty("streams", out var s) ? s.EnumerateArray().ToList() : new List<JsonElement>();

        // Localizar stream de vídeo e stream de áudio
        JsonElement videoStream = default;
        JsonElement audioStream = default;

        foreach (var stream in streams)
        {
            if (stream.TryGetProperty("codec_type", out var type))
            {
                string typeStr = type.GetString() ?? "";
                if (typeStr.Equals("video", StringComparison.OrdinalIgnoreCase) && videoStream.ValueKind == JsonValueKind.Undefined)
                {
                    videoStream = stream;
                }
                else if (typeStr.Equals("audio", StringComparison.OrdinalIgnoreCase) && audioStream.ValueKind == JsonValueKind.Undefined)
                {
                    audioStream = stream;
                }
            }
        }

        if (videoStream.ValueKind == JsonValueKind.Undefined && audioStream.ValueKind == JsonValueKind.Undefined)
        {
            // Saída válida sem streams de mídia: não há o que medir.
            return ExtractedMediaInfo.Unavailable(filePath, MediaMetadataStatus.Failed);
        }

        string codec = "unknown";
        int width = 0;
        int height = 0;
        double frameRate = 0.0;
        double averageFrameRate = 0.0;
        RationalTimeBase timeBase = ExtractedMediaInfo.UnknownTimeBase;
        bool isVfr = false;
        int rotation = 0;
        string? camera = null;
        string? gps = null;

        if (videoStream.ValueKind != JsonValueKind.Undefined)
        {
            if (videoStream.TryGetProperty("codec_name", out var cn))
                codec = cn.GetString() ?? "unknown";

            if (videoStream.TryGetProperty("width", out var w) && w.TryGetInt32(out int wv))
                width = wv;

            if (videoStream.TryGetProperty("height", out var h) && h.TryGetInt32(out int hv))
                height = hv;

            if (videoStream.TryGetProperty("r_frame_rate", out var rfr))
            {
                double rRate = ParseRate(rfr.GetString());
                if (rRate > 0) frameRate = rRate;
            }

            if (videoStream.TryGetProperty("avg_frame_rate", out var afr))
            {
                double aRate = ParseRate(afr.GetString());
                if (aRate > 0 && frameRate > 0 && Math.Abs(aRate - frameRate) > 0.05)
                {
                    // VFR: a taxa reportada passa a ser a média (avg_frame_rate), representativa para conformação.
                    isVfr = true;
                    averageFrameRate = aRate;
                }
            }

            // Fallback do TimeBase usa a MESMA taxa que vai para FrameRate (média em VFR), nunca o r_frame_rate nominal.
            double recordedRate = isVfr ? averageFrameRate : frameRate;
            if (videoStream.TryGetProperty("time_base", out var tb))
            {
                timeBase = ParseTimeBase(tb.GetString(), recordedRate);
            }
            else
            {
                timeBase = TimeBaseFromFps(recordedRate);
            }

            // Rotação em side_data_list ou tags
            if (videoStream.TryGetProperty("side_data_list", out var sideData))
            {
                foreach (var sd in sideData.EnumerateArray())
                {
                    if (sd.TryGetProperty("rotation", out var rot) && rot.TryGetInt32(out int rv))
                    {
                        rotation = rv;
                        break;
                    }
                }
            }
        }

        int audioChannels = 0;
        int audioSampleRate = 0;

        if (audioStream.ValueKind != JsonValueKind.Undefined)
        {
            if (codec == "unknown" && audioStream.TryGetProperty("codec_name", out var acn))
                codec = acn.GetString() ?? "unknown";

            if (audioStream.TryGetProperty("channels", out var ch) && ch.TryGetInt32(out int chv))
                audioChannels = chv;

            if (audioStream.TryGetProperty("sample_rate", out var sr) && int.TryParse(sr.GetString(), out int sRate))
                audioSampleRate = sRate;
        }

        double durationSecs = 0.0;
        if (format.ValueKind != JsonValueKind.Undefined && format.TryGetProperty("duration", out var durProp))
        {
            if (double.TryParse(durProp.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                durationSecs = d;
        }

        if (durationSecs <= 0 && videoStream.ValueKind != JsonValueKind.Undefined && videoStream.TryGetProperty("duration", out var vDurProp))
        {
            if (double.TryParse(vDurProp.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                durationSecs = d;
        }

        // Tags do container e do stream para data de criação, câmera e GPS
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (format.ValueKind != JsonValueKind.Undefined && format.TryGetProperty("tags", out var fTags))
        {
            foreach (var prop in fTags.EnumerateObject())
                tags[prop.Name] = prop.Value.GetString() ?? "";
        }

        if (videoStream.ValueKind != JsonValueKind.Undefined && videoStream.TryGetProperty("tags", out var vTags))
        {
            foreach (var prop in vTags.EnumerateObject())
                if (!tags.ContainsKey(prop.Name))
                    tags[prop.Name] = prop.Value.GetString() ?? "";
        }

        var (creationUtc, evidence) = ResolveCreationDate(tags, filePath);

        if (tags.TryGetValue("model", out var camModel) || tags.TryGetValue("com.apple.quicktime.model", out camModel))
            camera = camModel;

        if (tags.TryGetValue("location", out var loc) || tags.TryGetValue("location-eng", out loc))
            gps = loc;

        long durationTicks = timeBase.Numerator > 0 && durationSecs > 0
            ? (long)Math.Round(durationSecs * timeBase.Denominator / (double)timeBase.Numerator)
            : 0;

        return new ExtractedMediaInfo(
            Codec: codec,
            Width: width,
            Height: height,
            TimeBase: timeBase,
            FrameRate: Math.Round(isVfr ? averageFrameRate : frameRate, 2),
            DurationSeconds: Math.Round(durationSecs, 3),
            DurationTicks: durationTicks,
            AudioChannels: audioChannels,
            AudioSampleRate: audioSampleRate,
            CreationTimeUtc: creationUtc,
            SourceDateEvidence: evidence,
            IsVariableFrameRate: isVfr,
            RotationDegrees: rotation,
            CameraModel: camera,
            GpsLocation: gps,
            MetadataStatus: MediaMetadataStatus.Ok
        );
    }

    private static (DateTime? UtcTime, string Evidence) ResolveCreationDate(IReadOnlyDictionary<string, string> tags, string filePath)
    {
        // 1. Tags QuickTime com timezone explícito
        if (tags.TryGetValue("com.apple.quicktime.creationdate", out var qtDate) && !string.IsNullOrWhiteSpace(qtDate))
        {
            if (DateTimeOffset.TryParse(qtDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
                return (dto.UtcDateTime, "quicktime_creationdate_tag");
        }

        // 2. Tag padrão creation_time
        if (tags.TryGetValue("creation_time", out var cTime) && !string.IsNullOrWhiteSpace(cTime))
        {
            if (DateTimeOffset.TryParse(cTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                return (dto.UtcDateTime, "creation_time_utc_tag");
        }

        // 3. Tag genérica de data
        if (tags.TryGetValue("date", out var dVal) && !string.IsNullOrWhiteSpace(dVal))
        {
            if (DateTimeOffset.TryParse(dVal, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
                return (dto.UtcDateTime, "generic_date_tag");
        }

        // 4. Fallback seguro: FileSystem LastWriteTimeUtc
        try
        {
            var fi = new FileInfo(filePath);
            if (fi.Exists)
                return (fi.LastWriteTimeUtc, "file_system_last_write_fallback");
        }
        catch { }

        return (null, "no_timestamp_found");
    }

    /// <summary>
    /// Fallback sem ffprobe: nenhum valor de mídia é estimado. Ver <see cref="ExtractedMediaInfo.Unavailable"/>.
    /// </summary>
    public ExtractedMediaInfo ExtractFallback(string filePath, string status = MediaMetadataStatus.FfprobeUnavailable)
        => ExtractedMediaInfo.Unavailable(filePath, status);

    private static double ParseRate(string? rate)
    {
        if (string.IsNullOrWhiteSpace(rate)) return 0.0;

        var parts = rate.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out double num) &&
            double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double den) &&
            den > 0)
        {
            return num / den;
        }

        if (double.TryParse(rate, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
            return val;

        return 0.0;
    }

    private static RationalTimeBase TimeBaseFromFps(double fps)
        => fps > 0 ? RationalTimeBase.FromFps(fps) : ExtractedMediaInfo.UnknownTimeBase;

    private static RationalTimeBase ParseTimeBase(string? timeBase, double fps)
    {
        if (string.IsNullOrWhiteSpace(timeBase))
            return TimeBaseFromFps(fps);

        var parts = timeBase.Split('/');
        if (parts.Length == 2 &&
            long.TryParse(parts[0], out long num) &&
            long.TryParse(parts[1], out long den) &&
            num > 0 && den > 0)
        {
            return new RationalTimeBase(num, den);
        }

        return TimeBaseFromFps(fps);
    }
}
