using TravelCut.Application.Audio;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;

namespace TravelCut.Infrastructure.Analysis;

public static class PipelineStages
{
    public const string Transcription = "transcription";
    public const string Silence = "silence";
    public const string Hesitation = "hesitation";
    public const string Scene = "scene";
    public const string Candidates = "candidates";

    /// <summary>Ordem canônica de execução por mídia.</summary>
    public static readonly string[] Ordered = [Transcription, Silence, Hesitation, Scene, Candidates];

    public static string Label(string stage) => stage switch
    {
        Transcription => "Transcrição (Whisper large-v3)",
        Silence => "Detecção de silêncios",
        Hesitation => "Hesitações e repetições",
        Scene => "Classificação de cenas",
        Candidates => "Candidatos a corte",
        _ => stage
    };
}

public static class StageStates
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string CompletedWithIssues = "completed_with_issues";
    public const string Unavailable = "unavailable";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
    public const string Skipped = "skipped";

    public static bool IsTerminal(string state) => state is Completed or CompletedWithIssues or Unavailable or Failed or Canceled or Skipped;

    /// <summary>Converte o status de um serviço (completed|unavailable|failed|canceled) em estado de etapa.</summary>
    public static string FromOutcome(string? status) => status switch
    {
        "completed" => Completed,
        "unavailable" => Unavailable,
        "canceled" => Canceled,
        _ => Failed
    };
}

/// <summary>Estado de uma mídia dentro de um job. Mutado apenas sob <see cref="PipelineJobRun.Sync"/>.</summary>
public sealed class PipelineMediaWork
{
    public required Guid MediaId { get; init; }
    public required string RelativePath { get; init; }
    /// <summary>Caminho absoluto dentro da pasta do projeto; nulo quando o caminho é inválido.</summary>
    public required string? AbsolutePath { get; init; }
    public required MediaAsset Asset { get; init; }
    public string? PathError { get; init; }
    public Dictionary<string, string> States { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Reasons { get; } = new(StringComparer.Ordinal);
    public bool FromCache { get; set; }
}

/// <summary>Estado de um job de análise. Todas as leituras/escritas de estado passam por <see cref="Sync"/>.</summary>
public sealed class PipelineJobRun
{
    public PipelineJobRun(Guid jobId, Guid projectId, IReadOnlyList<string> stages, string language, bool force)
    {
        JobId = jobId;
        ProjectId = projectId;
        Stages = stages;
        Language = language;
        Force = force;
    }

    public Guid JobId { get; }
    public Guid ProjectId { get; }
    public IReadOnlyList<string> Stages { get; }
    public string Language { get; }
    public bool Force { get; }
    public string ProjectRoot { get; set; } = string.Empty;
    public List<PipelineMediaWork> Media { get; } = new();
    public CancellationTokenSource Cts { get; } = new();
    public Task Completion { get; set; } = Task.CompletedTask;

    // Trava entre processos ("analysis") mantida enquanto o job roda. Liberada uma única vez (TakeAnalysisLock).
    private IAsyncDisposable? _analysisLock;

    public void SetAnalysisLock(IAsyncDisposable analysisLock) => Volatile.Write(ref _analysisLock, analysisLock);

    /// <summary>Devolve a trava (uma única vez) para quem vai liberá-la; chamadas seguintes recebem null.</summary>
    public IAsyncDisposable? TakeAnalysisLock() => Interlocked.Exchange(ref _analysisLock, null);

    public object Sync { get; } = new();
    public string State { get; private set; } = "queued";
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public string? CurrentMediaRelativePath { get; private set; }
    private const int MaxWhisperExecutions = 50;
    private readonly List<WhisperExecutionRecord> _whisperExecutions = new();
    public string? CurrentStage { get; private set; }
    public string? CurrentDetail { get; private set; }
    public double? CurrentPercent { get; private set; }
    public DateTime? LastActivityAtUtc { get; private set; }

    public bool IsFinished
    {
        get
        {
            lock (Sync)
            {
                return State is "completed" or "completed_with_issues" or "failed" or "canceled";
            }
        }
    }

    public void InitializeMedia(IEnumerable<PipelineMediaWork> media)
    {
        lock (Sync)
        {
            Media.Clear();
            foreach (var work in media)
            {
                foreach (var stage in Stages)
                {
                    work.States[stage] = StageStates.Pending;
                }
                Media.Add(work);
            }
        }
    }

    public void MarkRunning()
    {
        lock (Sync)
        {
            State = "running";
            StartedAtUtc = DateTime.UtcNow;
            LastActivityAtUtc = StartedAtUtc;
        }
    }

    public void SetCurrentMedia(string? relativePath)
    {
        lock (Sync)
        {
            CurrentMediaRelativePath = relativePath;
            LastActivityAtUtc = DateTime.UtcNow;
        }
    }

    /// <summary>Novo registro auditável para a execução do worker desta mídia (mantém só as últimas).</summary>
    /// <summary>P10: transcrições do lote (um processo do worker, modelo carregado uma vez) por mídia; consumidas na ordem do job.</summary>
    public Dictionary<Guid, Task<TranscriptionOutcome>> BatchTranscriptions { get; } = new();

    /// <summary>P10: mídia cuja transcrição o job está aguardando; só ela repassa telemetria (o worker já pode estar na seguinte).</summary>
    public Guid AwaitingTranscriptionMediaId { get; set; }

    public WhisperExecutionRecord BeginWhisperExecution(PipelineMediaWork media)
    {
        var record = new WhisperExecutionRecord(JobId, media.MediaId);
        lock (Sync)
        {
            _whisperExecutions.Add(record);
            if (_whisperExecutions.Count > MaxWhisperExecutions) _whisperExecutions.RemoveAt(0);
        }
        return record;
    }

    public PipelineDiagnosticsDto DiagnosticsSnapshot()
    {
        List<WhisperExecutionRecord> records;
        lock (Sync)
        {
            records = _whisperExecutions.ToList();
        }
        return new PipelineDiagnosticsDto(JobId, records.Select(r => r.ToDto()).OfType<WhisperExecutionDto>().ToList());
    }

    /// <summary>Telemetria do worker durante a etapa atual. Depois do fim do job é ignorada.</summary>
    public void ReportTranscriptionProgress(TranscriptionProgressUpdate update)
    {
        lock (Sync)
        {
            if (FinishedAtUtc.HasValue) return;
            CurrentDetail = update.Stage switch
            {
                "loading_audio" => "Decodificando o áudio",
                "loading_model" => "Carregando o modelo",
                "transcribing" => "Transcrevendo",
                _ => CurrentDetail
            };
            CurrentPercent = update.Percent;
            LastActivityAtUtc = DateTime.UtcNow;
        }
    }

    public void SetStage(PipelineMediaWork media, string stage, string state, string? reason)
    {
        lock (Sync)
        {
            media.States[stage] = state;
            if (reason != null) media.Reasons[stage] = reason;
            else media.Reasons.Remove(stage);

            if (state == StageStates.Running) CurrentStage = stage;
            CurrentDetail = null;
            CurrentPercent = null;
            LastActivityAtUtc = DateTime.UtcNow;
        }
    }

    public void MarkFromCache(PipelineMediaWork media)
    {
        lock (Sync)
        {
            media.FromCache = true;
        }
    }

    /// <summary>Falha todas as etapas solicitadas da mídia (caminho inválido, arquivo ausente, erro inesperado).</summary>
    public void FailStages(PipelineMediaWork media, string reason, bool onlyNonTerminal)
    {
        lock (Sync)
        {
            foreach (var stage in Stages)
            {
                if (onlyNonTerminal && StageStates.IsTerminal(media.States[stage])) continue;
                media.States[stage] = StageStates.Failed;
                media.Reasons[stage] = reason;
            }
        }
    }

    /// <summary>Cancelamento: etapas em andamento ou pendentes viram canceled; etapas concluídas são preservadas.</summary>
    public void CancelNonTerminal()
    {
        lock (Sync)
        {
            foreach (var media in Media)
            {
                foreach (var stage in Stages)
                {
                    var state = media.States[stage];
                    if (state is StageStates.Pending or StageStates.Running)
                    {
                        media.States[stage] = StageStates.Canceled;
                        media.Reasons[stage] = "Análise cancelada pelo usuário.";
                    }
                }
            }
        }
    }

    public void FailNonTerminal(string reason)
    {
        lock (Sync)
        {
            foreach (var media in Media)
            {
                foreach (var stage in Stages)
                {
                    var state = media.States[stage];
                    if (state is StageStates.Pending or StageStates.Running)
                    {
                        media.States[stage] = StageStates.Failed;
                        media.Reasons[stage] = reason;
                    }
                }
            }
        }
    }

    /// <summary>Define o estado final: canceled > failed (todas as etapas falharam) > completed_with_issues > completed.</summary>
    public void Finish(string? forcedState)
    {
        lock (Sync)
        {
            if (forcedState != null)
            {
                State = forcedState;
            }
            else
            {
                var all = Media.SelectMany(m => m.States.Values).ToList();
                if (all.Count > 0 && all.All(s => s == StageStates.Failed))
                {
                    State = "failed";
                }
                else if (all.All(s => s == StageStates.Completed))
                {
                    State = "completed";
                }
                else
                {
                    State = "completed_with_issues";
                }
            }
            FinishedAtUtc = DateTime.UtcNow;
            CurrentMediaRelativePath = null;
            CurrentStage = null;
            CurrentDetail = null;
            CurrentPercent = null;
            LastActivityAtUtc = FinishedAtUtc;
        }
    }

    public PipelineStatusDto Snapshot(AnalysisEnvironmentDto? environment)
    {
        lock (Sync)
        {
            var stages = Stages.Select(stage =>
            {
                var items = Media.Select(m => (State: m.States[stage], Reason: m.Reasons.GetValueOrDefault(stage))).ToList();
                string aggregate = Aggregate(items.Select(i => i.State).ToList());
                string? reason = aggregate == StageStates.Completed
                    ? null
                    : items.Where(i => i.State != StageStates.Completed).Select(i => i.Reason).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));

                return new PipelineStageStatusDto(
                    Stage: stage,
                    Label: PipelineStages.Label(stage),
                    State: aggregate,
                    ProcessedItems: items.Count(i => StageStates.IsTerminal(i.State)),
                    TotalItems: Media.Count,
                    Reason: reason);
            }).ToList();

            var media = Media.Select(m => new PipelineMediaStatusDto(
                MediaId: m.MediaId,
                RelativePath: m.RelativePath,
                StageStates: Stages.ToDictionary(s => s, s => m.States[s], StringComparer.Ordinal),
                FromCache: m.FromCache,
                Message: string.Join(" | ", m.Reasons.Values.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct()) is { Length: > 0 } msg ? msg : null
            )).ToList();

            return new PipelineStatusDto(
                JobId: JobId,
                ProjectId: ProjectId,
                State: State,
                Stages: stages,
                Media: media,
                CurrentMedia: CurrentMediaRelativePath,
                Environment: environment,
                StartedAtUtc: StartedAtUtc,
                FinishedAtUtc: FinishedAtUtc,
                Activity: new PipelineActivityDto(
                    CurrentStage: CurrentStage,
                    CurrentStageLabel: CurrentStage is null ? null : PipelineStages.Label(CurrentStage),
                    Detail: CurrentDetail,
                    Percent: CurrentPercent,
                    LastActivityAtUtc: LastActivityAtUtc,
                    CancelRequested: Cts.IsCancellationRequested,
                    MediaCompleted: Media.Count(m => m.States.Values.All(StageStates.IsTerminal)),
                    MediaTotal: Media.Count));
        }
    }

    private static string Aggregate(List<string> states)
    {
        if (states.Count == 0) return StageStates.Skipped;
        if (states.All(s => s == StageStates.Pending)) return StageStates.Pending;
        if (states.Any(s => s is StageStates.Pending or StageStates.Running)) return StageStates.Running;

        if (states.All(s => s == StageStates.Skipped)) return StageStates.Skipped;
        if (states.Any(s => s == StageStates.Canceled)) return StageStates.Canceled;

        var meaningful = states.Where(s => s != StageStates.Skipped).ToList();
        if (meaningful.All(s => s == StageStates.Unavailable)) return StageStates.Unavailable;
        if (meaningful.All(s => s == StageStates.Failed)) return StageStates.Failed;
        if (states.All(s => s == StageStates.Completed)) return StageStates.Completed;
        return StageStates.CompletedWithIssues;
    }
}

/// <summary>Estado de análise de uma mídia em construção; começa com o último resultado persistido no cache.</summary>
public sealed class MediaAnalysisWork
{
    public string TranscriptionStatus { get; set; } = "not_run";
    public string? TranscriptionReason { get; set; }
    public TranscriptDto? Transcript { get; set; }
    public string SilenceStatus { get; set; } = "not_run";
    public string? SilenceReason { get; set; }
    public List<VadSilenceSegmentDto> Silences { get; set; } = new();
    public List<HesitationCandidateDto> Hesitations { get; set; } = new();
    public SceneClassificationDto? Scene { get; set; }
    public List<EditSuggestionDto> CutCandidates { get; set; } = new();

    public static MediaAnalysisWork From(MediaAnalysisDto? previous)
    {
        if (previous == null) return new MediaAnalysisWork();
        return new MediaAnalysisWork
        {
            TranscriptionStatus = previous.TranscriptionStatus,
            TranscriptionReason = previous.TranscriptionReason,
            Transcript = previous.Transcript,
            SilenceStatus = previous.SilenceStatus,
            SilenceReason = previous.SilenceReason,
            Silences = previous.Silences.ToList(),
            Hesitations = previous.Hesitations.ToList(),
            Scene = previous.Scene,
            CutCandidates = previous.CutCandidates.ToList()
        };
    }

    public MediaAnalysisDto ToDto(Guid mediaId, DateTime analyzedAtUtc) => new(
        MediaId: mediaId,
        TranscriptionStatus: TranscriptionStatus,
        TranscriptionReason: TranscriptionReason,
        Transcript: Transcript,
        SilenceStatus: SilenceStatus,
        SilenceReason: SilenceReason,
        Silences: Silences,
        Hesitations: Hesitations,
        Scene: Scene,
        CutCandidates: CutCandidates,
        AnalyzedAtUtc: analyzedAtUtc);
}
