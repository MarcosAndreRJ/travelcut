using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Audio;

// Resultado do parse do stderr do silencedetect (função pura, testável por fixture).
public sealed record SilenceStderrParseResult(
    List<VadSilenceSegmentDto> Segments,
    int DiscardedOpenSilences,
    double? MediaDurationSeconds
);

// Regras:
// - Silêncio vem exclusivamente do FFmpeg silencedetect. Sem FFmpeg -> "unavailable"; nunca estima.
// - Hesitações vêm exclusivamente da transcrição real (palavras com timestamps do Whisper).
// - Sempre subprocesso com ArgumentList (sem shell), timeout e cancelamento via IProcessRunner.
public class SilenceDetectionService : ISilenceDetectionService
{
    public const string FfmpegEnvVar = "TRAVELCUT_FFMPEG_PATH";
    public const string FfmpegMissingReason =
        "FFmpeg não encontrado. Defina TRAVELCUT_FFMPEG_PATH ou instale o FFmpeg no PATH. Nenhum silêncio foi estimado.";

    private const double MinPauseSeconds = 0.3;
    private const double PauseToleranceSeconds = 0.1;
    private const double LongWordSeconds = 1.0;
    private const double ProlongedWordSeconds = 0.8;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultSilenceTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ProbeRetryAfter = TimeSpan.FromSeconds(30);

    private static readonly string[] CommonFfmpegPaths =
    {
        @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
        @"C:\ffmpeg\bin\ffmpeg.exe"
    };

    private static readonly Regex SilenceStartRegex = new(
        @"silence_start:\s*(-?[0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SilenceEndRegex = new(
        @"silence_end:\s*(-?[0-9]+(?:\.[0-9]+)?)\s*\|\s*silence_duration:\s*(-?[0-9]+(?:\.[0-9]+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DurationRegex = new(
        @"Duration:\s*([0-9]{2,}):([0-9]{2}):([0-9]{2}(?:\.[0-9]+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex NonLetterRegex = new(@"\P{L}+", RegexOptions.Compiled);

    private static readonly Regex LaughterRegex = new(
        @"^(?:kk+|(?:ha|he|hi|ho|hu){2,}h?|h?(?:ha|he|hi|ho|hu)h?|(?:ah){2,}h?|(?:rs)+|(?:hue)+h?)$",
        RegexOptions.Compiled);

    private static readonly Regex SoundFillerRegex = new(
        @"^(?:hum+|u?hm+|ahn+|hã+|é{2,})$", RegexOptions.Compiled);

    private static readonly Regex RepeatedLetterRegex = new(@"(\p{L})\1\1", RegexOptions.Compiled);

    // E10: resposta/negação/ênfase duplicada seguida de continuação ("não, não foi isso") não é repetição removível.
    private static readonly HashSet<string> EmphasisTokens = new(StringComparer.Ordinal)
    {
        "não", "nao", "sim", "nunca", "jamais", "nada", "claro", "tá", "ta", "ok", "muito", "bem", "já", "ja", "mesmo", "sempre",
    };

    // Artigos de uma letra duplicados ("o o carro") continuam candidatos a repetição.
    private static readonly HashSet<string> SingleLetterStutter = new(StringComparer.Ordinal) { "o", "a", "e" };

    private static readonly HashSet<string> PeraCueSingle = new(StringComparer.Ordinal) { "peraí", "perai" };
    private static readonly HashSet<string> PeraCueAfterNao = new(StringComparer.Ordinal) { "pera", "peraí", "perai" };

    private readonly ILogger<SilenceDetectionService> _logger;
    private readonly IProcessRunner _runner;
    private readonly string? _explicitFfmpegPath;
    private readonly TimeSpan _silenceTimeout;
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private FfmpegProbe? _probe;
    private DateTime _probeAtUtc;

    private sealed record FfmpegProbe(string? Executable, string? Version);

    public SilenceDetectionService(
        ILogger<SilenceDetectionService> logger,
        IProcessRunner? processRunner = null,
        string? ffmpegPath = null,
        TimeSpan? silenceTimeout = null)
    {
        _logger = logger;
        _runner = processRunner ?? new ProcessRunner();
        _explicitFfmpegPath = string.IsNullOrWhiteSpace(ffmpegPath) ? null : ffmpegPath.Trim().Trim('"');
        _silenceTimeout = silenceTimeout ?? DefaultSilenceTimeout;
    }

    public bool IsFfmpegAvailable()
    {
        return GetProbeAsync(CancellationToken.None).GetAwaiter().GetResult().Executable is not null;
    }

    public string? GetFfmpegVersion()
    {
        return GetProbeAsync(CancellationToken.None).GetAwaiter().GetResult().Version;
    }

    public async Task<SilenceDetectionOutcome> DetectSilenceAsync(
        string mediaPath,
        SilenceDetectionOptions? options = null,
        CancellationToken ct = default)
    {
        var opts = options ?? new SilenceDetectionOptions();
        if (!AreOptionsValid(opts))
        {
            return Failed("Parâmetros de detecção de silêncio inválidos (limiar deve ser finito e <= 0 dB; duração mínima > 0 s).");
        }

        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return Failed("Caminho de mídia vazio para análise de silêncio.");
        }

        FfmpegProbe probe;
        try
        {
            probe = await GetProbeAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Canceled();
        }

        if (probe.Executable is null)
        {
            return new SilenceDetectionOutcome("unavailable", new List<VadSilenceSegmentDto>(), FfmpegMissingReason);
        }

        if (!File.Exists(mediaPath))
        {
            return Failed("Arquivo de mídia não encontrado para análise de silêncio.");
        }

        var fullPath = Path.GetFullPath(mediaPath);
        var run = await _runner.RunAsync(
            new ProcessRunRequest(probe.Executable, BuildSilenceDetectArguments(fullPath, opts), _silenceTimeout),
            ct).ConfigureAwait(false);

        switch (run.Status)
        {
            case ProcessRunStatus.Canceled:
                return Canceled();
            case ProcessRunStatus.TimedOut:
                return Failed("Tempo limite excedido na detecção de silêncio (FFmpeg).");
            case ProcessRunStatus.NotStarted:
                return Failed("Não foi possível iniciar o FFmpeg para detecção de silêncio.");
        }

        if (run.ExitCode != 0)
        {
            return Failed($"FFmpeg terminou com código {run.ExitCode} na detecção de silêncio.");
        }

        var parsed = ParseSilencedetectStderr(run.StandardError, opts.NoiseToleranceDb, opts.MinSilenceDurationSeconds);
        string? note = parsed.DiscardedOpenSilences > 0
            ? $"{parsed.DiscardedOpenSilences} silêncio(s) aberto(s) no fim descartado(s): duração da mídia não informada pelo FFmpeg."
            : null;

        return new SilenceDetectionOutcome("completed", parsed.Segments, note);
    }

    public List<HesitationCandidateDto> DetectHesitationsAndFillers(
        TranscriptDto transcript,
        IReadOnlyList<VadSilenceSegmentDto> silences)
    {
        var result = new List<HesitationCandidateDto>();
        if (transcript.Words is null || transcript.Words.Count == 0)
        {
            return result;
        }

        var words = transcript.Words
            .OrderBy(w => w.StartSeconds)
            .ThenBy(w => w.EndSeconds)
            .ToList();

        var tokens = words.Select(w => Normalize(w.Text)).ToArray();
        var pauses = (silences ?? Array.Empty<VadSilenceSegmentDto>())
            .Where(s => s.DurationSeconds >= MinPauseSeconds)
            .ToList();

        var scan = new HesitationScan(words, tokens, pauses, transcript.AudioDurationSeconds);

        var i = 0;
        while (i < words.Count)
        {
            var token = tokens[i];
            if (token.Length == 0 || IsLaughter(token))
            {
                i++;
                continue;
            }

            var match = MatchAt(scan, i);
            if (match is null)
            {
                i++;
                continue;
            }

            var (type, span, band) = match.Value;
            result.Add(BuildCandidate(scan, i, span, type, band));
            i += span;
        }

        return result;
    }

    // ---- Argumentos e parse (públicos para teste) ----

    public static IReadOnlyList<string> BuildSilenceDetectArguments(string fullMediaPath, SilenceDetectionOptions options)
    {
        var noise = options.NoiseToleranceDb.ToString("0.###", CultureInfo.InvariantCulture);
        var minDur = options.MinSilenceDurationSeconds.ToString("0.###", CultureInfo.InvariantCulture);

        return new List<string>
        {
            "-hide_banner",
            "-nostats",
            "-i",
            fullMediaPath,
            "-vn",
            "-af",
            $"silencedetect=noise={noise}dB:d={minDur}",
            "-f",
            "null",
            "-"
        };
    }

    public static SilenceStderrParseResult ParseSilencedetectStderr(
        string stderr,
        double noiseDb,
        double minDurationSeconds = 0.0)
    {
        var segments = new List<VadSilenceSegmentDto>();
        var discarded = 0;
        var mediaDuration = ParseMediaDuration(stderr);
        double? pendingStart = null;

        foreach (var line in stderr.Split('\n'))
        {
            var endMatch = SilenceEndRegex.Match(line);
            if (endMatch.Success
                && TryParseInvariant(endMatch.Groups[1].Value, out var endVal)
                && TryParseInvariant(endMatch.Groups[2].Value, out var durVal))
            {
                var startVal = pendingStart ?? Math.Max(0.0, endVal - durVal);
                pendingStart = null;
                if (endVal > startVal)
                {
                    segments.Add(MakeSegment(startVal, endVal, noiseDb));
                }
                continue;
            }

            var startMatch = SilenceStartRegex.Match(line);
            if (startMatch.Success && TryParseInvariant(startMatch.Groups[1].Value, out var startParsed))
            {
                pendingStart = Math.Max(0.0, startParsed);
            }
        }

        // Silêncio aberto no fim: só fecha se a duração da mídia foi informada pelo FFmpeg.
        if (pendingStart is { } open)
        {
            if (mediaDuration is { } total)
            {
                var tailDuration = total - open;
                if (tailDuration > 0 && tailDuration >= minDurationSeconds)
                {
                    segments.Add(MakeSegment(open, total, noiseDb));
                }
            }
            else
            {
                discarded++;
            }
        }

        return new SilenceStderrParseResult(segments, discarded, mediaDuration);
    }

    public static string ConfidenceBandForDuration(double durationSeconds) =>
        durationSeconds >= 2.0 ? "high" : durationSeconds >= 1.0 ? "medium" : "low";

    // ---- FFmpeg: localização e probe com cache ----

    private async Task<FfmpegProbe> GetProbeAsync(CancellationToken ct)
    {
        // Encontrado fica em cache; ausente é reconsultado após 30 s (FFmpeg pode ser instalado com o app aberto).
        if (_probe is { } cached && (cached.Executable is not null || DateTime.UtcNow - _probeAtUtc < ProbeRetryAfter))
        {
            return cached;
        }

        await _probeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_probe is { } again && (again.Executable is not null || DateTime.UtcNow - _probeAtUtc < ProbeRetryAfter))
            {
                return again;
            }

            var resolved = await ResolveFfmpegAsync(ct).ConfigureAwait(false);
            _probe = resolved;
            _probeAtUtc = DateTime.UtcNow;
            return resolved;
        }
        finally
        {
            _probeGate.Release();
        }
    }

    private async Task<FfmpegProbe> ResolveFfmpegAsync(CancellationToken ct)
    {
        foreach (var candidate in EnumerateFfmpegCandidates())
        {
            var run = await _runner.RunAsync(
                new ProcessRunRequest(candidate, new[] { "-version" }, ProbeTimeout),
                ct).ConfigureAwait(false);

            if (run.Status == ProcessRunStatus.Canceled || ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }

            if (run.Status == ProcessRunStatus.Completed
                && run.ExitCode == 0
                && run.StandardOutput.Contains("ffmpeg version", StringComparison.OrdinalIgnoreCase))
            {
                var firstLine = run.StandardOutput.Split('\n')[0].Trim();
                _logger.LogInformation("FFmpeg disponível: {Version}", firstLine);
                return new FfmpegProbe(candidate, firstLine);
            }
        }

        return new FfmpegProbe(null, null);
    }

    private IEnumerable<string> EnumerateFfmpegCandidates()
    {
        if (_explicitFfmpegPath is not null)
        {
            yield return _explicitFfmpegPath;
            yield break;
        }

        var fromEnv = Environment.GetEnvironmentVariable(FfmpegEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            yield return fromEnv.Trim().Trim('"');
        }

        foreach (var common in CommonFfmpegPaths)
        {
            if (File.Exists(common))
            {
                yield return common;
            }
        }

        // Último recurso: resolvido pelo sistema via PATH.
        yield return "ffmpeg";
    }

    // ---- Hesitações ----

    private readonly record struct HesitationMatch(string Type, int Span, string Band);

    private static HesitationMatch? MatchAt(HesitationScan s, int i)
    {
        var t = s.Token(i);
        var next = s.Token(i + 1);
        var next2 = s.Token(i + 2);

        // 1) Expressões multi-palavra (falso início e filler composto).
        if (t == "não" && i + 1 < s.Count && PeraCueAfterNao.Contains(next))
        {
            return new HesitationMatch("false_start", 2, "medium");
        }

        if (PeraCueSingle.Contains(t))
        {
            return new HesitationMatch("false_start", 1, "medium");
        }

        if (t == "vou" && next == "de" && next2 == "novo" && i + 2 < s.Count)
        {
            return new HesitationMatch("false_start", 3, s.PauseAfter(i + 2) ? "medium" : "low");
        }

        if (t == "deixa" && next == "eu" && i + 1 < s.Count)
        {
            return new HesitationMatch("false_start", 2, s.PauseAfter(i + 1) ? "medium" : "low");
        }

        if (t == "tipo" && next == "assim" && i + 1 < s.Count)
        {
            return new HesitationMatch("filler", 2, s.AmbiguousBand(i, i + 1));
        }

        // 2) Repetições imediatas (bigrama e palavra), sem contar risadas.
        if (i + 3 < s.Count
            && t.Length >= 2 && next.Length >= 2
            && t == next2 && next == s.Token(i + 3)
            && !IsLaughter(next2) && !IsLaughter(s.Token(i + 3)))
        {
            return new HesitationMatch("repetition", 4, s.PauseBetween(i + 1) ? "medium" : "low");
        }

        if (i + 1 < s.Count && t == next && EmphasisTokens.Contains(t))
        {
            // Rebaixado: possível ênfase/resposta; vai para revisão e nunca é tratado como repetição removível.
            // Vale também para o par no fim da transcrição (sem palavra depois): "não não" final continua ênfase.
            return new HesitationMatch("emphasis", 2, "low");
        }

        if (i + 1 < s.Count && (t.Length >= 2 || SingleLetterStutter.Contains(t)) && t == next && !IsLaughter(next))
        {
            return new HesitationMatch("repetition", 2, s.PauseBetween(i) ? "medium" : "low");
        }

        // 3) Fillers lexicais ambíguos: nunca "high".
        if (t == "tipo" || t == "né")
        {
            return new HesitationMatch("filler", 1, s.AmbiguousBand(i, i));
        }

        if (t == "então")
        {
            return s.PauseBefore(i) && s.PauseAfter(i)
                ? new HesitationMatch("filler", 1, "medium")
                : null;
        }

        // 4) Fillers sonoros puros: high só quando isolados por pausa.
        if (SoundFillerRegex.IsMatch(t))
        {
            return new HesitationMatch("filler", 1, s.PauseBefore(i) && s.PauseAfter(i) ? "high" : "medium");
        }

        // "é" também é verbo: só conta quando isolado por pausa ou muito alongado.
        if (t == "é")
        {
            if (s.PauseBefore(i) && s.PauseAfter(i))
            {
                return new HesitationMatch("filler", 1, "high");
            }

            if (s.Duration(i) >= LongWordSeconds)
            {
                return new HesitationMatch("filler", 1, "medium");
            }

            return null;
        }

        // 5) Prolongação por letras repetidas (>= 3).
        if (RepeatedLetterRegex.IsMatch(t))
        {
            var band = s.Duration(i) >= ProlongedWordSeconds || s.PauseBefore(i) || s.PauseAfter(i)
                ? "medium"
                : "low";
            return new HesitationMatch("prolongation", 1, band);
        }

        return null;
    }

    private static HesitationCandidateDto BuildCandidate(HesitationScan s, int start, int span, string type, string band)
    {
        var end = start + span - 1;

        var detected = new StringBuilder();
        for (var k = start; k <= end; k++)
        {
            if (k > start) detected.Append(' ');
            detected.Append(s.Text(k));
        }

        var before = new StringBuilder();
        for (var k = Math.Max(0, start - 6); k < start; k++)
        {
            if (before.Length > 0) before.Append(' ');
            before.Append(s.Text(k));
        }

        var after = new StringBuilder();
        for (var k = end + 1; k < Math.Min(s.Count, end + 7); k++)
        {
            if (after.Length > 0) after.Append(' ');
            after.Append(s.Text(k));
        }

        var needsReview = !(type == "filler" && band == "high");

        return new HesitationCandidateDto(
            StartSeconds: s.Start(start),
            EndSeconds: s.End(end),
            DetectedWord: detected.ToString(),
            Type: type,
            ConfidenceBand: band,
            ContextBefore: before.ToString(),
            ContextAfter: after.ToString(),
            NeedsHumanReview: needsReview
        );
    }

    private static bool IsLaughter(string token) => LaughterRegex.IsMatch(token);

    private static string Normalize(string text)
    {
        var lowered = (text ?? string.Empty).Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        return NonLetterRegex.Replace(lowered, string.Empty);
    }

    private sealed class HesitationScan
    {
        private readonly IReadOnlyList<TranscriptWordDto> _words;
        private readonly string[] _tokens;
        private readonly List<VadSilenceSegmentDto> _pauses;
        private readonly double _mediaEnd;

        public HesitationScan(IReadOnlyList<TranscriptWordDto> words, string[] tokens, List<VadSilenceSegmentDto> pauses, double mediaEnd)
        {
            _words = words;
            _tokens = tokens;
            _pauses = pauses;
            _mediaEnd = mediaEnd;
        }

        public int Count => _words.Count;
        public string Token(int i) => i >= 0 && i < _tokens.Length ? _tokens[i] : string.Empty;
        public string Text(int i) => _words[i].Text.Trim();
        public double Start(int i) => _words[i].StartSeconds;
        public double End(int i) => _words[i].EndSeconds;
        public double Duration(int i) => _words[i].EndSeconds - _words[i].StartSeconds;

        public bool PauseBefore(int i)
        {
            var from = i > 0 ? _words[i - 1].EndSeconds : 0.0;
            return HasPause(from, _words[i].StartSeconds);
        }

        public bool PauseAfter(int j)
        {
            var from = _words[j].EndSeconds;
            var to = j + 1 < Count ? _words[j + 1].StartSeconds : Math.Max(from, _mediaEnd);
            return HasPause(from, to);
        }

        public bool PauseBetween(int k) => HasPause(_words[k].EndSeconds, _words[k + 1].StartSeconds);

        public string AmbiguousBand(int first, int last) =>
            PauseBefore(first) || PauseAfter(last) ? "medium" : "low";

        // Pausa = gap entre timestamps de palavras >= 0,3 s, ou silêncio (>= 0,3 s) que cobre o intervalo.
        private bool HasPause(double from, double to)
        {
            if (to <= from)
            {
                return false;
            }

            if (to - from >= MinPauseSeconds)
            {
                return true;
            }

            foreach (var p in _pauses)
            {
                if (p.StartSeconds <= from + PauseToleranceSeconds && p.EndSeconds >= to - PauseToleranceSeconds)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // ---- Utilitários ----

    private static bool AreOptionsValid(SilenceDetectionOptions o) =>
        double.IsFinite(o.NoiseToleranceDb) && o.NoiseToleranceDb <= 0
        && double.IsFinite(o.MinSilenceDurationSeconds) && o.MinSilenceDurationSeconds > 0;

    private static VadSilenceSegmentDto MakeSegment(double start, double end, double noiseDb)
    {
        var duration = end - start;
        return new VadSilenceSegmentDto(
            StartSeconds: Math.Round(start, 3),
            EndSeconds: Math.Round(end, 3),
            DurationSeconds: Math.Round(duration, 3),
            NoiseDb: noiseDb,
            Confidence: ConfidenceBandForDuration(duration)
        );
    }

    private static double? ParseMediaDuration(string stderr)
    {
        var m = DurationRegex.Match(stderr);
        if (!m.Success) return null;
        if (!int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hours)) return null;
        if (!int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)) return null;
        if (!TryParseInvariant(m.Groups[3].Value, out var seconds)) return null;
        return hours * 3600.0 + minutes * 60.0 + seconds;
    }

    private static bool TryParseInvariant(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private static SilenceDetectionOutcome Failed(string reason) =>
        new("failed", new List<VadSilenceSegmentDto>(), reason);

    private static SilenceDetectionOutcome Canceled() =>
        new("canceled", new List<VadSilenceSegmentDto>(), "Análise de silêncio cancelada.");
}
