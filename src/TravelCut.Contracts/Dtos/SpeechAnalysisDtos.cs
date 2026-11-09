using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

// Contrato da análise audiovisual (F05). Regras:
// - Nada é sintetizado: sem FFmpeg/Python/Whisper o resultado é "unavailable" com motivo, nunca dados estimados.
// - Heurísticas usam faixas qualitativas ("low" | "medium" | "high") + evidências, não percentuais.
// - Tempos em segundos no timestamp da mídia fonte (não timeline).
// Métodos IPC: analysis.environment, pipeline.start, pipeline.status, pipeline.cancel, analysis.getMedia, suggestions.list.

public record VadSilenceSegmentDto(
    [property: JsonPropertyName("startSeconds")] double StartSeconds,
    [property: JsonPropertyName("endSeconds")] double EndSeconds,
    [property: JsonPropertyName("durationSeconds")] double DurationSeconds,
    [property: JsonPropertyName("noiseDb")] double NoiseDb,           // limiar usado no silencedetect
    [property: JsonPropertyName("confidence")] string Confidence      // low | medium | high
);

public record HesitationCandidateDto(
    [property: JsonPropertyName("startSeconds")] double StartSeconds,
    [property: JsonPropertyName("endSeconds")] double EndSeconds,
    [property: JsonPropertyName("detectedWord")] string DetectedWord,
    [property: JsonPropertyName("type")] string Type,                 // hesitation | false_start | filler | prolongation | repetition
    [property: JsonPropertyName("confidenceBand")] string ConfidenceBand, // low | medium | high
    [property: JsonPropertyName("contextBefore")] string ContextBefore,   // até ~6 palavras antes
    [property: JsonPropertyName("contextAfter")] string ContextAfter,     // até ~6 palavras depois
    [property: JsonPropertyName("needsHumanReview")] bool NeedsHumanReview
);

public record SceneClassificationDto(
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("category")] string Category,         // conversa | estrada | paisagem | loja | drone | outro | indeterminado
    [property: JsonPropertyName("confidenceBand")] string ConfidenceBand, // low | medium | high
    [property: JsonPropertyName("isHypothesis")] bool IsHypothesis,   // true quando inferido sem evidência forte
    [property: JsonPropertyName("evidence")] List<string> Evidence,   // ex.: "metadado: câmera DJI", "fala em 62% da duração (transcrição)"
    [property: JsonPropertyName("hasSpeech")] bool? HasSpeech          // null quando não há transcrição nem VAD
);

// ---- Resultados de serviço (Application) ----

public record TranscriptionOutcome(
    [property: JsonPropertyName("status")] string Status,             // completed | unavailable | failed | canceled
    [property: JsonPropertyName("transcript")] TranscriptDto? Transcript,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("engine")] string? Engine,            // ex.: "openai-whisper 20240930" (do worker), null se indisponível
    [property: JsonPropertyName("modelId")] string? ModelId,          // ex.: "large-v3"
    [property: JsonPropertyName("failureCode")] string? FailureCode = null // P12: ver TranscriptionFailureCodes; null = sem classificação
);

/// <summary>Classificação estável de falhas do worker Whisper (P12): guia retry e fallback de dispositivo.</summary>
public static class TranscriptionFailureCodes
{
    /// <summary>Falha provavelmente passageira (worker sem resposta, processo encerrado): 1 retry automático.</summary>
    public const string Transient = "transient";
    /// <summary>Memória da GPU esgotada: fallback automático para CPU (nunca troca de modelo).</summary>
    public const string CudaOutOfMemory = "cuda_out_of_memory";
}

public record SilenceDetectionOutcome(
    [property: JsonPropertyName("status")] string Status,             // completed | unavailable | failed | canceled
    [property: JsonPropertyName("segments")] List<VadSilenceSegmentDto> Segments,
    [property: JsonPropertyName("reason")] string? Reason
);

// ---- Ambiente ----

public record AnalysisEnvironmentDto(
    [property: JsonPropertyName("ffmpegAvailable")] bool FfmpegAvailable,
    [property: JsonPropertyName("ffmpegVersion")] string? FfmpegVersion,
    [property: JsonPropertyName("pythonAvailable")] bool PythonAvailable,
    [property: JsonPropertyName("whisperAvailable")] bool WhisperAvailable,
    [property: JsonPropertyName("whisperEngine")] string? WhisperEngine,
    [property: JsonPropertyName("whisperModel")] string WhisperModel,  // sempre "large-v3" (não substituir sem autorização)
    [property: JsonPropertyName("cudaAvailable")] bool? CudaAvailable, // null = desconhecido
    [property: JsonPropertyName("reasons")] List<string> Reasons,      // por que algo está indisponível
    // Aditivos (F14.8/P04): ausentes em respostas antigas; consumidores devem tratá-los como opcionais.
    [property: JsonPropertyName("device")] string? Device = null,                  // "cuda" | "cpu" | null (desconhecido)
    [property: JsonPropertyName("modelCached")] bool? ModelCached = null,          // modelo large-v3 no cache local; null = desconhecido
    [property: JsonPropertyName("cpuFallback")] bool CpuFallback = false,          // Whisper disponível, mas sem CUDA (CPU: muito lento)
    [property: JsonPropertyName("whisperUnavailableReason")] string? WhisperUnavailableReason = null // motivo(s) quando whisperAvailable=false
);

// ---- Pipeline ----

public record StartPipelineRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("mediaIds")] List<Guid>? MediaIds,     // null = todas as mídias de vídeo/áudio do projeto
    [property: JsonPropertyName("stages")] List<string>? Stages,       // null = todas: transcription, silence, hesitation, scene, candidates
    [property: JsonPropertyName("language")] string? Language,         // null = "pt"
    [property: JsonPropertyName("force")] bool Force                   // true = ignora cache
);

public record StartPipelineResponse(
    [property: JsonPropertyName("jobId")] Guid JobId
);

public record PipelineJobRequest(
    [property: JsonPropertyName("jobId")] Guid JobId
);

public record ProjectRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId
);

public record PipelineStageStatusDto(
    [property: JsonPropertyName("stage")] string Stage,               // transcription | silence | hesitation | scene | candidates
    [property: JsonPropertyName("label")] string Label,               // PT-BR para UI
    [property: JsonPropertyName("state")] string State,               // pending | running | completed | completed_with_issues | unavailable | failed | canceled | skipped
    [property: JsonPropertyName("processedItems")] int ProcessedItems,
    [property: JsonPropertyName("totalItems")] int TotalItems,        // denominador real (mídias desta etapa)
    [property: JsonPropertyName("reason")] string? Reason
);

public record PipelineMediaStatusDto(
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("stageStates")] Dictionary<string, string> StageStates, // stage -> state
    [property: JsonPropertyName("fromCache")] bool FromCache,
    [property: JsonPropertyName("message")] string? Message
);

public record PipelineStatusDto(
    [property: JsonPropertyName("jobId")] Guid? JobId,               // null = nenhum job neste projeto
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("state")] string State,               // idle | queued | running | completed | completed_with_issues | failed | canceled
    [property: JsonPropertyName("stages")] List<PipelineStageStatusDto> Stages,
    [property: JsonPropertyName("media")] List<PipelineMediaStatusDto> Media,
    [property: JsonPropertyName("currentMedia")] string? CurrentMedia, // relativePath em processamento
    [property: JsonPropertyName("environment")] AnalysisEnvironmentDto? Environment,
    [property: JsonPropertyName("startedAtUtc")] DateTime? StartedAtUtc,
    [property: JsonPropertyName("finishedAtUtc")] DateTime? FinishedAtUtc,
    [property: JsonPropertyName("activity")] PipelineActivityDto? Activity = null,
    [property: JsonPropertyName("postAnalysis")] PostAnalysisStatusDto? PostAnalysis = null   // F14.8: processamento automático pós-análise (null = sem orquestrador)
);

/// <summary>
/// Atividade interna do job. ProcessedItems/TotalItems contam mídias concluídas; aqui fica o que acontece dentro da mídia.
/// Percent só vem de telemetria real do motor (null = sem dado; a UI mostra progresso indeterminado).
/// LastActivityAtUtc é o último sinal confirmado; ausência de sinal não significa processo travado.
/// </summary>
public record PipelineActivityDto(
    [property: JsonPropertyName("currentStage")] string? CurrentStage,
    [property: JsonPropertyName("currentStageLabel")] string? CurrentStageLabel,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("percent")] double? Percent,
    [property: JsonPropertyName("lastActivityAtUtc")] DateTime? LastActivityAtUtc,
    [property: JsonPropertyName("cancelRequested")] bool CancelRequested,
    [property: JsonPropertyName("mediaCompleted")] int MediaCompleted,
    [property: JsonPropertyName("mediaTotal")] int MediaTotal
);

/// <summary>
/// Diagnóstico técnico do worker Whisper (pipeline.diagnostics). Contém caminhos locais: só é pedido por ação explícita
/// do usuário e não sai pelo MCP. Logs sem conteúdo da transcrição e limitados às últimas linhas.
/// </summary>
public record PipelineDiagnosticsDto(
    [property: JsonPropertyName("jobId")] Guid? JobId,
    [property: JsonPropertyName("executions")] List<WhisperExecutionDto> Executions
);

public record WhisperExecutionDto(
    [property: JsonPropertyName("jobId")] Guid JobId,
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("requestId")] string RequestId,
    [property: JsonPropertyName("mediaPath")] string MediaPath,
    [property: JsonPropertyName("startedAtUtc")] DateTime? StartedAtUtc,
    [property: JsonPropertyName("finishedAtUtc")] DateTime? FinishedAtUtc,
    [property: JsonPropertyName("pythonExecutable")] string PythonExecutable,
    [property: JsonPropertyName("arguments")] List<string> Arguments,
    [property: JsonPropertyName("workerScript")] string WorkerScript,
    [property: JsonPropertyName("workingDirectory")] string WorkingDirectory,
    [property: JsonPropertyName("environmentVariables")] Dictionary<string, string> EnvironmentVariables,
    [property: JsonPropertyName("standardInput")] string StandardInput,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("device")] string? Device,
    [property: JsonPropertyName("processState")] string ProcessState, // running | completed | failed | canceled | timed_out | not_started
    [property: JsonPropertyName("exitCode")] int? ExitCode,
    [property: JsonPropertyName("canceled")] bool Canceled,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("logLines")] List<WhisperLogLineDto> LogLines,
    [property: JsonPropertyName("droppedLogLines")] int DroppedLogLines,
    [property: JsonPropertyName("diagnosticScript")] string DiagnosticScript
);

public record WhisperLogLineDto(
    [property: JsonPropertyName("atUtc")] DateTime AtUtc,
    [property: JsonPropertyName("stream")] string Stream,
    [property: JsonPropertyName("text")] string Text
);

public record CancelPipelineResponse(
    [property: JsonPropertyName("canceled")] bool Canceled
);

// ---- Resultado por mídia ----

public record MediaAnalysisRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("mediaId")] Guid MediaId
);

public record MediaAnalysisDto(
    [property: JsonPropertyName("mediaId")] Guid MediaId,
    [property: JsonPropertyName("transcriptionStatus")] string TranscriptionStatus, // not_run | completed | unavailable | failed
    [property: JsonPropertyName("transcriptionReason")] string? TranscriptionReason,
    [property: JsonPropertyName("transcript")] TranscriptDto? Transcript,
    [property: JsonPropertyName("silenceStatus")] string SilenceStatus,             // not_run | completed | unavailable | failed
    [property: JsonPropertyName("silenceReason")] string? SilenceReason,
    [property: JsonPropertyName("silences")] List<VadSilenceSegmentDto> Silences,
    [property: JsonPropertyName("hesitations")] List<HesitationCandidateDto> Hesitations,
    [property: JsonPropertyName("scene")] SceneClassificationDto? Scene,
    [property: JsonPropertyName("cutCandidates")] List<EditSuggestionDto> CutCandidates,
    [property: JsonPropertyName("analyzedAtUtc")] DateTime? AnalyzedAtUtc
);
