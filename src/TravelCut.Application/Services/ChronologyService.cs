using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;

namespace TravelCut.Application.Services;

public record ChronologyItem(
    Guid MediaId,
    string RelativePath,
    MediaKind Kind,
    string? CameraModel,
    DateTime RecordedAtUtc,
    double DurationSeconds,
    DateTime EstimatedEndAtUtc,
    string TimestampSource,
    string Confidence,
    int SessionIndex,
    string? Anomaly
);

public record ChronologySession(
    int SessionIndex,
    string Name,
    DateTime StartTimeUtc,
    DateTime EndTimeUtc,
    int TotalClips,
    double TotalDurationSeconds,
    IReadOnlyList<string> CameraModels,
    IReadOnlyList<ChronologyItem> Clips
);

public record ChronologyReport(
    IReadOnlyList<ChronologySession> Sessions,
    int TotalClips,
    double TotalDurationSeconds,
    DateTime? EarliestUtc,
    DateTime? LatestUtc,
    int AnomaliesDetected
);

public interface IChronologyService
{
    ChronologyReport BuildChronology(
        IEnumerable<(MediaAsset Asset, MediaMetadata Metadata)> mediaItems,
        TimeSpan? sessionGapThreshold = null
    );
}

public class ChronologyService : IChronologyService
{
    private readonly TimeSpan _defaultGapThreshold = TimeSpan.FromMinutes(15);

    public ChronologyReport BuildChronology(
        IEnumerable<(MediaAsset Asset, MediaMetadata Metadata)> mediaItems,
        TimeSpan? sessionGapThreshold = null)
    {
        var itemsList = mediaItems.ToList();
        if (itemsList.Count == 0)
        {
            return new ChronologyReport(
                Sessions: Array.Empty<ChronologySession>(),
                TotalClips: 0,
                TotalDurationSeconds: 0,
                EarliestUtc: null,
                LatestUtc: null,
                AnomaliesDetected: 0
            );
        }

        var threshold = sessionGapThreshold ?? _defaultGapThreshold;

        // 1. Extração preliminar de horários e confiabilidade
        var rawList = itemsList.Select(pair =>
        {
            var asset = pair.Asset;
            var meta = pair.Metadata;

            DateTime recordedAt;
            string source;
            string confidence;

            if (meta.CreationTimeUtc.HasValue)
            {
                recordedAt = meta.CreationTimeUtc.Value;
                source = string.IsNullOrWhiteSpace(meta.SourceDateEvidence) ? "metadata_creation_time" : meta.SourceDateEvidence;
                confidence = source.Contains("quicktime", StringComparison.OrdinalIgnoreCase) || source.Contains("gps", StringComparison.OrdinalIgnoreCase)
                    ? "High"
                    : "Medium";
            }
            else
            {
                recordedAt = asset.LastWriteUtc;
                source = "file_system_last_write_fallback";
                confidence = "Low";
            }

            double duration = Math.Max(0.1, meta.DurationSeconds);
            DateTime estimatedEnd = recordedAt.AddSeconds(duration);

            return new
            {
                Asset = asset,
                Metadata = meta,
                RecordedAt = recordedAt,
                Duration = duration,
                EstimatedEnd = estimatedEnd,
                Source = source,
                Confidence = confidence,
                Camera = !string.IsNullOrWhiteSpace(meta.CameraModel) ? meta.CameraModel : "Câmera Principal"
            };
        })
        .OrderBy(x => x.RecordedAt)
        .ThenBy(x => x.Asset.RelativePath, StringComparer.OrdinalIgnoreCase)
        .ToList();

        // 2. Agrupamento em Sessões/Tomadas e detecção de anomalias
        var sessions = new List<ChronologySession>();
        var currentSessionClips = new List<ChronologyItem>();
        int currentSessionIndex = 1;
        int totalAnomalies = 0;

        for (int i = 0; i < rawList.Count; i++)
        {
            var current = rawList[i];
            string? anomaly = null;

            if (i > 0)
            {
                var previous = rawList[i - 1];
                var gap = current.RecordedAt - previous.EstimatedEnd;

                // Anomalia: sobreposição temporal entre clipes
                if (current.RecordedAt < previous.EstimatedEnd - TimeSpan.FromSeconds(1))
                {
                    anomaly = $"Sobreposição temporal detectada com clipe anterior ({previous.Asset.RelativePath}).";
                    totalAnomalies++;
                }

                // Se o intervalo entre clipes for superior ao threshold, fecha a sessão e inicia outra
                if (gap > threshold)
                {
                    if (currentSessionClips.Count > 0)
                    {
                        sessions.Add(CreateSession(currentSessionIndex, currentSessionClips));
                        currentSessionIndex++;
                        currentSessionClips = new List<ChronologyItem>();
                    }
                }
            }

            var item = new ChronologyItem(
                MediaId: current.Asset.MediaId,
                RelativePath: current.Asset.RelativePath,
                Kind: current.Asset.Kind,
                CameraModel: current.Camera,
                RecordedAtUtc: current.RecordedAt,
                DurationSeconds: current.Duration,
                EstimatedEndAtUtc: current.EstimatedEnd,
                TimestampSource: current.Source,
                Confidence: current.Confidence,
                SessionIndex: currentSessionIndex,
                Anomaly: anomaly
            );

            currentSessionClips.Add(item);
        }

        if (currentSessionClips.Count > 0)
        {
            sessions.Add(CreateSession(currentSessionIndex, currentSessionClips));
        }

        double totalDuration = rawList.Sum(x => x.Duration);
        DateTime? earliest = rawList.Count > 0 ? rawList.First().RecordedAt : null;
        DateTime? latest = rawList.Count > 0 ? rawList.Last().EstimatedEnd : null;

        return new ChronologyReport(
            Sessions: sessions,
            TotalClips: rawList.Count,
            TotalDurationSeconds: Math.Round(totalDuration, 2),
            EarliestUtc: earliest,
            LatestUtc: latest,
            AnomaliesDetected: totalAnomalies
        );
    }

    private static ChronologySession CreateSession(int index, List<ChronologyItem> clips)
    {
        var start = clips.First().RecordedAtUtc;
        var end = clips.Max(c => c.EstimatedEndAtUtc);
        double duration = clips.Sum(c => c.DurationSeconds);
        var cameras = clips.Select(c => c.CameraModel ?? "Câmera Padrão").Distinct().ToList();

        string sessionName = $"Sessão {index:D2} — {start:dd/MM/yyyy HH:mm}";

        return new ChronologySession(
            SessionIndex: index,
            Name: sessionName,
            StartTimeUtc: start,
            EndTimeUtc: end,
            TotalClips: clips.Count,
            TotalDurationSeconds: Math.Round(duration, 2),
            CameraModels: cameras,
            Clips: clips.ToList()
        );
    }
}
