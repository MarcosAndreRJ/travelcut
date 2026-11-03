// =====================================================================================================
// FIXTURES DE PORTA — NÃO SÃO PROVA DE MÍDIA REAL.
//
// Substituem SOMENTE as três portas que dependem de ferramentas externas nesta máquina:
//   - IFfprobeExtractor            (metadados: FFprobe)
//   - IWhisperTranscriptionService (transcrição: Python + Whisper large-v3)
//   - ISilenceDetectionService     (silêncios: FFmpeg silencedetect)
// Todo o resto (repositórios EF/SQLite, IpcDispatcher, PipelineJobManager, planejador, editor em modo mock,
// cache, trava de arquivo) é o código real montado por AddTravelCutCore.
//
// Os valores são fixos e sintéticos. Nada aqui valida FFprobe, FFmpeg, Whisper ou DaVinci.
// A única lógica não substituída nesta classe é a heurística léxica de hesitações
// (SilenceDetectionService.DetectHesitationsAndFillers), que é pura e não executa processo.
// =====================================================================================================

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.ValueObjects;
using TravelCut.Infrastructure.Audio;

namespace TravelCut.UnitTests.EndToEnd;

/// <summary>Dados fixos de uma mídia sintética (nome do arquivo em 1-Bruto é a chave).</summary>
public sealed record E2EMediaSpec(
    string FileName,
    ExtractedMediaInfo Metadata,
    IReadOnlyList<TranscriptWordDto> Words,
    IReadOnlyList<VadSilenceSegmentDto> Silences);

public sealed class E2EPortFixtures : IFfprobeExtractor, IWhisperTranscriptionService, ISilenceDetectionService
{
    private readonly Dictionary<string, E2EMediaSpec> _specs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _ffprobeCalls = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _whisperCalls = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _silenceCalls = new(StringComparer.OrdinalIgnoreCase);
    private readonly SilenceDetectionService _hesitationHeuristics = new(NullLogger<SilenceDetectionService>.Instance);
    private readonly TaskCompletionSource _silenceEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Quando true, DetectSilenceAsync fica "lento": aguarda até o cancelamento do job (cenário de cancelamento).</summary>
    public bool HoldSilence { get; set; }

    /// <summary>Concluída quando uma chamada de silêncio entra no modo lento (HoldSilence).</summary>
    public Task SilenceEntered => _silenceEntered.Task;

    public E2EPortFixtures Add(E2EMediaSpec spec)
    {
        _specs[spec.FileName] = spec;
        return this;
    }

    public int FfprobeCalls(string fileName) => _ffprobeCalls.GetValueOrDefault(fileName);
    public int WhisperCalls(string fileName) => _whisperCalls.GetValueOrDefault(fileName);
    public int SilenceCalls(string fileName) => _silenceCalls.GetValueOrDefault(fileName);

    // ---- IFfprobeExtractor ------------------------------------------------------------------------

    // Fixture: finge disponibilidade porque este fake é o substituto do extrator real (só usado por InspectCandidates).
    public bool IsFfprobeAvailable() => true;

    public Task<ExtractedMediaInfo> ExtractMetadataAsync(string filePath, CancellationToken ct = default)
    {
        string name = Path.GetFileName(filePath);
        Count(_ffprobeCalls, name);
        var info = _specs.TryGetValue(name, out var spec)
            ? spec.Metadata
            : ExtractedMediaInfo.Unavailable(filePath, MediaMetadataStatus.Failed);
        return Task.FromResult(info);
    }

    // ---- IWhisperTranscriptionService -------------------------------------------------------------

    // Fixture: o ambiente real desta VM não tem Python/Whisper; a sondagem reporta isso com honestidade.
    public Task<WhisperProbeResult> ProbeEnvironmentAsync(CancellationToken ct = default) =>
        Task.FromResult(new WhisperProbeResult(
            HasPython: false,
            HasCuda: null,
            Device: null,
            HasWhisper: false,
            Engine: null,
            DefaultModel: "large-v3",
            StatusSummary: "Fixture: Python/Whisper não sondados nesta máquina de teste."));

    public Task<TranscriptionOutcome> TranscribeAsync(
        string mediaPath,
        Guid mediaId,
        string model = "large-v3",
        string language = "pt",
        CancellationToken ct = default)
    {
        string name = Path.GetFileName(mediaPath);
        Count(_whisperCalls, name);
        if (!_specs.TryGetValue(name, out var spec) || spec.Words.Count == 0)
        {
            return Task.FromResult(new TranscriptionOutcome("unavailable", null,
                "Fixture sem transcrição para este arquivo.", null, null));
        }

        var transcript = new TranscriptDto(
            TranscriptId: Deterministic("transcript:" + name),
            MediaId: mediaId,
            ModelId: model,
            Language: language,
            FullText: string.Join(' ', spec.Words.Select(w => w.Text)),
            AudioDurationSeconds: spec.Metadata.DurationSeconds,
            Words: spec.Words.ToList());
        return Task.FromResult(new TranscriptionOutcome("completed", transcript, null, "fixture-whisper", model));
    }

    // ---- ISilenceDetectionService -----------------------------------------------------------------

    // Fixture: sem FFmpeg nesta VM. A detecção de silêncios vem da fixture; a sondagem continua honesta.
    public bool IsFfmpegAvailable() => false;

    public string? GetFfmpegVersion() => null;

    public async Task<SilenceDetectionOutcome> DetectSilenceAsync(
        string mediaPath,
        SilenceDetectionOptions? options = null,
        CancellationToken ct = default)
    {
        string name = Path.GetFileName(mediaPath);
        Count(_silenceCalls, name);

        if (HoldSilence)
        {
            _silenceEntered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }

        if (!_specs.TryGetValue(name, out var spec))
        {
            return new SilenceDetectionOutcome("unavailable", new List<VadSilenceSegmentDto>(),
                "Fixture sem silêncios para este arquivo.");
        }
        return new SilenceDetectionOutcome("completed", spec.Silences.ToList(), null);
    }

    public List<HesitationCandidateDto> DetectHesitationsAndFillers(
        TranscriptDto transcript,
        IReadOnlyList<VadSilenceSegmentDto> silences) =>
        _hesitationHeuristics.DetectHesitationsAndFillers(transcript, silences);

    // ---- Helpers de construção (determinísticos) --------------------------------------------------

    /// <summary>Metadados de vídeo CFR 29,97 fps (base 1001/30000), status "ok".</summary>
    public static ExtractedMediaInfo Cfr2997(double durationSeconds) => Video(durationSeconds, isVfr: false);

    /// <summary>
    /// Metadados VFR. Fixture: taxa média 29,97 (o extrator usa avg_frame_rate em VFR; o r_frame_rate nominal 60
    /// não é campo de ExtractedMediaInfo). Base de tempo segue a mesma taxa média, como no extrator.
    /// </summary>
    public static ExtractedMediaInfo Vfr2997(double durationSeconds) => Video(durationSeconds, isVfr: true);

    private static ExtractedMediaInfo Video(double durationSeconds, bool isVfr)
    {
        var timeBase = RationalTimeBase.Fps2997;
        long ticks = (long)Math.Round(durationSeconds * timeBase.Denominator / (double)timeBase.Numerator);
        return new ExtractedMediaInfo(
            Codec: "h264",
            Width: 1920,
            Height: 1080,
            TimeBase: timeBase,
            FrameRate: 29.97,
            DurationSeconds: durationSeconds,
            DurationTicks: ticks,
            AudioChannels: 2,
            AudioSampleRate: 48000,
            CreationTimeUtc: new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            SourceDateEvidence: "fixture:creation_time",
            IsVariableFrameRate: isVfr,
            RotationDegrees: 0,
            CameraModel: "Fixture Cam",
            GpsLocation: null,
            MetadataStatus: MediaMetadataStatus.Ok);
    }

    public static TranscriptWordDto Word(int seq, double start, double end, string text) =>
        new(seq, start, end, text, 0.9);

    /// <summary>Silêncio com confiança "medium": nunca vira corte automático, sempre pede revisão humana.</summary>
    public static VadSilenceSegmentDto Silence(double start, double end) =>
        new(start, end, end - start, -30.0, "medium");

    private static void Count(ConcurrentDictionary<string, int> counter, string name) =>
        counter.AddOrUpdate(name, 1, (_, n) => n + 1);

    private static Guid Deterministic(string seed) => new(MD5.HashData(Encoding.UTF8.GetBytes(seed)));
}
