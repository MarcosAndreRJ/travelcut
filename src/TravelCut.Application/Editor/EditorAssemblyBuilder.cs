using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Planning;

namespace TravelCut.Application.Editor;

/// <summary>Dados de uma mídia necessários à montagem. Montado pelo use case (caminho já validado dentro de 1-Bruto/2-Assets).</summary>
/// <param name="SourceFpsNum">Frames por segundo da fonte = SourceFpsNum/SourceFpsDen; 0 quando sem taxa válida (→ INVALID_TIMEBASE).</param>
public sealed record EditorMediaSource(
    Guid MediaId,
    string RelativePath,
    string AbsolutePath,
    long SourceFpsNum,
    long SourceFpsDen,
    bool IsVariableFrameRate,
    long DurationUs);

/// <summary>Resultado do builder: manifesto ou erro estável (sem exceção para falhas esperadas).</summary>
public sealed record EditorAssemblyBuildResult(EditorAssemblyDto? Assembly, string? ErrorCode, string? ErrorMessage);

/// <summary>
/// Converte o plano editorial em manifesto de montagem (editor-assembly/1). Função pura: sem I/O, sem relógio
/// (o instante UTC é recebido por parâmetro). Regras de escopo (contrato editor/1):
/// - Só a faixa V1 vira clipe (trackIndex 1, "video_audio"). A1 coberto por V1 incluído → aviso LINKED_AUDIO_COVERED.
/// - Demais faixas, velocidade ≠ 1/1 e transições ≠ "cut" são rejeitadas com CAPABILITY_UNSUPPORTED (sem deslocar os outros clipes).
/// - Frames da fonte = round_half_even(µs · fps) (ver <see cref="TimelineMath.MicrosecondsToFrameIndex"/>).
/// </summary>
public static class EditorAssemblyBuilder
{
    public const string SchemaVersion = "editor-assembly/1";
    public const string Notice = "Manifesto de montagem do TravelCut — não é um projeto DaVinci.";

    public const string VideoTrackId = "V1";
    public const string AudioTrackId = "A1";
    public const string VideoAudioMediaType = "video_audio";
    public const int MaxProjectNameLength = 40;

    public const string CapabilityUnsupportedCode = "CAPABILITY_UNSUPPORTED";
    public const string MediaNotFoundCode = "MEDIA_NOT_FOUND";
    public const string InvalidTimebaseCode = "INVALID_TIMEBASE";
    public const string EmptyIntervalCode = "EMPTY_INTERVAL";
    public const string VfrSourceCode = "VFR_SOURCE";
    public const string MixedFrameRateCode = "MIXED_FRAME_RATE";
    public const string FrameRoundingCode = "FRAME_ROUNDING";
    public const string LinkedAudioCoveredCode = "LINKED_AUDIO_COVERED";

    public static EditorAssemblyBuildResult Build(
        EditPlanDto plan,
        IReadOnlyDictionary<Guid, EditorMediaSource> media,
        string projectName,
        DateTime nowUtc)
    {
        var timelineRate = EditorTimebases.TimelineFrameRate(plan.TimeBase.FrameDurationNum, plan.TimeBase.FrameDurationDen);
        if (timelineRate is null)
        {
            return new EditorAssemblyBuildResult(null, InvalidTimebaseCode,
                "Base de tempo da timeline não suportada pelo editor (use 23,976; 24; 25; 29,97; 30; 50; 59,94 ou 60 fps).");
        }

        // Taxa da timeline em frames por segundo = 1 / frameDuration.
        long tlFpsNum = plan.TimeBase.FrameDurationDen;
        long tlFpsDen = plan.TimeBase.FrameDurationNum;

        var rejected = new List<EditorRejectedOpDto>();
        var warnings = new List<EditorWarningDto>();
        var clips = new List<EditorAssemblyClipDto>();
        var usedMedia = new Dictionary<Guid, EditorMediaSource>();

        foreach (var transition in plan.Transitions.Where(t => t.Kind != "cut"))
        {
            rejected.Add(new EditorRejectedOpDto("transition", transition.FromClipId, CapabilityUnsupportedCode,
                $"Transição '{transition.Kind}' não suportada nesta versão; o corte é mantido sem transição."));
        }

        var ordered = plan.Clips
            .OrderBy(c => c.TimelineStartFrame)
            .ThenBy(c => c.TrackId, StringComparer.Ordinal)
            .ThenBy(c => c.ClipId)
            .ToList();

        // Passo 1: vídeo V1 (define quais A1 estão cobertos).
        var includedVideo = new HashSet<Guid>();
        foreach (var clip in ordered.Where(c => c.TrackId == VideoTrackId))
        {
            var built = BuildVideoClip(clip, media, tlFpsNum, tlFpsDen, rejected, warnings, usedMedia);
            if (built is null) continue;
            clips.Add(built);
            includedVideo.Add(clip.ClipId);
        }

        // Passo 2: demais faixas.
        foreach (var clip in ordered.Where(c => c.TrackId != VideoTrackId))
        {
            if (clip.TrackId == AudioTrackId && clip.LinkedClipId is Guid linked && includedVideo.Contains(linked))
            {
                warnings.Add(new EditorWarningDto(LinkedAudioCoveredCode,
                    "Áudio do clipe V1 vinculado já incluído na montagem.", clip.ClipId));
                continue;
            }

            var message = clip.TrackId == AudioTrackId
                ? "Áudio sem vídeo V1 aplicado não é suportado nesta versão."
                : $"Faixa '{clip.TrackId}' não suportada nesta versão.";
            rejected.Add(new EditorRejectedOpDto("track", clip.ClipId, CapabilityUnsupportedCode, message));
        }

        var orderedClips = clips
            .OrderBy(c => c.RecordFrame)
            .ThenBy(c => c.ClipId)
            .ToList();

        var mediaList = usedMedia.Values
            .OrderBy(m => m.MediaId)
            .Select(m => new EditorAssemblyMediaDto(m.MediaId, m.RelativePath, m.AbsolutePath,
                m.SourceFpsNum, m.SourceFpsDen, m.IsVariableFrameRate, m.DurationUs))
            .ToList();

        var editorProjectName = EditorProjectName(projectName, plan.Revision, nowUtc);
        var timelineName = TimelineName(plan.Revision);

        var fingerprint = ComputeFingerprint(plan, timelineRate, mediaList, orderedClips, rejected, warnings);

        var assembly = new EditorAssemblyDto(
            SchemaVersion: SchemaVersion,
            Notice: Notice,
            ProjectId: plan.ProjectId,
            PlanId: plan.PlanId,
            Revision: plan.Revision,
            PlanFingerprint: plan.PlanFingerprint,
            EditorProjectName: editorProjectName,
            TimelineName: timelineName,
            TimeBase: plan.TimeBase,
            TimelineFrameRate: timelineRate,
            Media: mediaList,
            Clips: orderedClips,
            Rejected: rejected,
            Warnings: warnings,
            AssemblyFingerprint: fingerprint);

        return new EditorAssemblyBuildResult(assembly, null, null);
    }

    /// <summary>"TravelCut_&lt;nome&gt;_r&lt;NNNN&gt;_&lt;aaaammddThhmmss UTC&gt;". Nome: só [A-Za-z0-9_-], máx. 40.</summary>
    public static string EditorProjectName(string projectName, int revision, DateTime nowUtc)
    {
        var stamp = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        return $"TravelCut_{SanitizeName(projectName)}_r{revision:D4}_{stamp}";
    }

    public static string TimelineName(int revision) => $"TravelCut r{revision:D4}";

    /// <summary>Mantém [A-Za-z0-9_-]; demais caracteres viram '_'. Vazio → "Projeto". Máximo de 40 caracteres.</summary>
    public static string SanitizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Projeto";
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            if (sb.Length >= MaxProjectNameLength) break;
            sb.Append(ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-' ? ch : '_');
        }
        return sb.Length == 0 ? "Projeto" : sb.ToString();
    }

    private static EditorAssemblyClipDto? BuildVideoClip(
        PlanClipDto clip,
        IReadOnlyDictionary<Guid, EditorMediaSource> media,
        long tlFpsNum,
        long tlFpsDen,
        List<EditorRejectedOpDto> rejected,
        List<EditorWarningDto> warnings,
        Dictionary<Guid, EditorMediaSource> usedMedia)
    {
        // Velocidade ≠ 1 não é suportada: o clipe sai da montagem e os demais mantêm recordFrame (lacuna declarada).
        if (clip.SpeedNum != clip.SpeedDen)
        {
            rejected.Add(new EditorRejectedOpDto("speed", clip.ClipId, CapabilityUnsupportedCode,
                $"Velocidade {clip.SpeedNum}/{clip.SpeedDen} não suportada nesta versão; o trecho não foi incluído na montagem."));
            return null;
        }

        if (!media.TryGetValue(clip.MediaId, out var src))
        {
            rejected.Add(new EditorRejectedOpDto("clip", clip.ClipId, MediaNotFoundCode,
                "Mídia do clipe não encontrada no projeto ou fora das pastas de origem (1-Bruto/2-Assets)."));
            return null;
        }

        if (src.SourceFpsNum <= 0 || src.SourceFpsDen <= 0)
        {
            rejected.Add(new EditorRejectedOpDto("clip", clip.ClipId, InvalidTimebaseCode,
                "Mídia sem taxa de quadros válida nos metadados."));
            return null;
        }

        if (clip.DurationFrames <= 0 || clip.SourceOutUs <= clip.SourceInUs)
        {
            rejected.Add(new EditorRejectedOpDto("clip", clip.ClipId, EmptyIntervalCode,
                "Trecho sem duração em quadros; não há o que montar."));
            return null;
        }

        long sourceStart = TimelineMath.MicrosecondsToFrameIndex(clip.SourceInUs, src.SourceFpsNum, src.SourceFpsDen);
        long sourceEnd = TimelineMath.MicrosecondsToFrameIndex(clip.SourceOutUs, src.SourceFpsNum, src.SourceFpsDen);

        if (EditorTimebases.SameRate(src.SourceFpsNum, src.SourceFpsDen, tlFpsNum, tlFpsDen))
        {
            // Mesma taxa: a duração do plano manda; o fim é forçado para start + duration.
            long forcedEnd = sourceStart + clip.DurationFrames;
            if (forcedEnd != sourceEnd)
            {
                warnings.Add(new EditorWarningDto(FrameRoundingCode,
                    $"Fim do trecho ajustado em {forcedEnd - sourceEnd} quadro(s) para manter a duração do plano.", clip.ClipId));
            }
            sourceEnd = forcedEnd;
        }
        else
        {
            warnings.Add(new EditorWarningDto(MixedFrameRateCode,
                "Taxa da fonte diferente da taxa da timeline; a conformação pelo Resolve não foi verificada; conferir no host.", clip.ClipId));
        }

        if (src.IsVariableFrameRate)
        {
            warnings.Add(new EditorWarningDto(VfrSourceCode,
                "Fonte com taxa variável; a conformação pelo Resolve não foi verificada; conferir no host.", clip.ClipId));
        }

        usedMedia[clip.MediaId] = src;

        return new EditorAssemblyClipDto(
            ClipId: clip.ClipId,
            MediaId: clip.MediaId,
            TrackIndex: 1,
            MediaType: VideoAudioMediaType,
            SourceStartFrame: sourceStart,
            SourceEndFrameExclusive: sourceEnd,
            RecordFrame: clip.TimelineStartFrame,
            DurationFrames: clip.DurationFrames,
            SourceInUs: clip.SourceInUs,
            SourceOutUs: clip.SourceOutUs);
    }

    /// <summary>
    /// SHA-256 hex do JSON canônico (ordem fixa de propriedades; listas já ordenadas). Exclui nomes com data e o aviso:
    /// dois dry-runs do mesmo plano têm o mesmo fingerprint; muda se algum clipe, mídia ou aviso mudar.
    /// </summary>
    private static string ComputeFingerprint(
        EditPlanDto plan,
        string timelineRate,
        List<EditorAssemblyMediaDto> media,
        List<EditorAssemblyClipDto> clips,
        List<EditorRejectedOpDto> rejected,
        List<EditorWarningDto> warnings)
    {
        var body = new FingerprintBody(
            SchemaVersion,
            plan.PlanId,
            plan.Revision,
            plan.PlanFingerprint,
            plan.TimeBase,
            timelineRate,
            media,
            clips,
            rejected,
            warnings);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(body);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed record FingerprintBody(
        string SchemaVersion,
        Guid PlanId,
        int Revision,
        string PlanFingerprint,
        TimelineTimeBaseDto TimeBase,
        string TimelineFrameRate,
        List<EditorAssemblyMediaDto> Media,
        List<EditorAssemblyClipDto> Clips,
        List<EditorRejectedOpDto> Rejected,
        List<EditorWarningDto> Warnings);
}
