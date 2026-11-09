using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

// Contrato da importação de mídia (Tela 07 — Importar mídia para o projeto).
// Métodos IPC: fs.listDirectory, dialog.selectFiles, media.inspectCandidates,
// media.getThumbnail, media.import.start, media.import.status, media.import.cancel.
// Valores desconhecidos são null — nunca estimados (ver CLAUDE.md "não inventar duração").

public record ListDirectoryRequest(
    [property: JsonPropertyName("path")] string? Path
);

public record DirectoryEntryDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("fullPath")] string FullPath,
    [property: JsonPropertyName("isDirectory")] bool IsDirectory,
    [property: JsonPropertyName("byteLength")] long? ByteLength,
    [property: JsonPropertyName("kind")] string? Kind // Video | Audio | Image | null (diretório)
);

public record ListDirectoryResponse(
    [property: JsonPropertyName("path")] string? Path,          // null = raiz (lista de unidades)
    [property: JsonPropertyName("parentPath")] string? ParentPath,
    [property: JsonPropertyName("entries")] IReadOnlyList<DirectoryEntryDto> Entries,
    [property: JsonPropertyName("truncated")] bool Truncated    // true quando a listagem foi cortada no limite de entradas
);

public record SelectFilesRequest(
    [property: JsonPropertyName("filter")] string? Filter // "video" | "assets" | null (todas as mídias)
);

public record SelectFilesResponse(
    [property: JsonPropertyName("paths")] IReadOnlyList<string> Paths,
    [property: JsonPropertyName("canceled")] bool Canceled
);

public record InspectCandidatesRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("paths")] IReadOnlyList<string> Paths // arquivos e/ou pastas (expandidas recursivamente)
);

public record ImportCandidateDto(
    [property: JsonPropertyName("fullPath")] string FullPath,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("originFolder")] string OriginFolder,     // nome da pasta pai
    [property: JsonPropertyName("kind")] string Kind,                     // Video | Audio | Image
    [property: JsonPropertyName("container")] string Container,           // extensão sem ponto, maiúscula (MP4, MOV...)
    [property: JsonPropertyName("byteLength")] long ByteLength,
    [property: JsonPropertyName("durationSeconds")] double? DurationSeconds,
    [property: JsonPropertyName("width")] int? Width,
    [property: JsonPropertyName("height")] int? Height,
    [property: JsonPropertyName("codec")] string? Codec,
    [property: JsonPropertyName("metadataStatus")] string MetadataStatus, // ok | ffprobe_unavailable | failed | not_requested
    [property: JsonPropertyName("insideProject")] bool InsideProject,
    [property: JsonPropertyName("alreadyInLibrary")] bool AlreadyInLibrary
);

public record InspectCandidatesResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<ImportCandidateDto> Items,
    [property: JsonPropertyName("ffprobeAvailable")] bool FfprobeAvailable,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors
);

public record GetThumbnailRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("atSeconds")] double? AtSeconds
);

public record ThumbnailResponse(
    [property: JsonPropertyName("status")] string Status, // ok | ffmpeg_unavailable | failed | not_supported
    [property: JsonPropertyName("dataUrl")] string? DataUrl,
    [property: JsonPropertyName("reason")] string? Reason
);

public record StartImportRequest(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("paths")] IReadOnlyList<string> Paths,
    [property: JsonPropertyName("destination")] string Destination,   // "1-Bruto" | "2-Assets"
    [property: JsonPropertyName("subfolder")] string? Subfolder,      // relativo ao destino; sem ".."
    [property: JsonPropertyName("copyFiles")] bool CopyFiles,
    [property: JsonPropertyName("generateThumbnails")] bool GenerateThumbnails,
    [property: JsonPropertyName("analyzeMetadata")] bool AnalyzeMetadata,
    [property: JsonPropertyName("detectSpeech")] bool DetectSpeech,
    [property: JsonPropertyName("tags")] IReadOnlyList<string> Tags
);

public record StartImportResponse(
    [property: JsonPropertyName("jobId")] Guid JobId
);

public record ImportJobRequest(
    [property: JsonPropertyName("jobId")] Guid JobId
);

public record ImportItemStatusDto(
    [property: JsonPropertyName("sourcePath")] string SourcePath,
    [property: JsonPropertyName("result")] string Result, // pending | copying | imported | renamed | registered_in_place | skipped_duplicate | failed | canceled
    [property: JsonPropertyName("destinationRelativePath")] string? DestinationRelativePath,
    [property: JsonPropertyName("message")] string? Message
);

public record ImportJobStatusDto(
    [property: JsonPropertyName("jobId")] Guid JobId,
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("state")] string State,         // queued | running | completed | completed_with_errors | failed | canceled
    [property: JsonPropertyName("stage")] string Stage,         // preflight | copy | verify | metadata | thumbnails | register | done
    [property: JsonPropertyName("totalFiles")] int TotalFiles,
    [property: JsonPropertyName("processedFiles")] int ProcessedFiles,
    [property: JsonPropertyName("bytesTotal")] long BytesTotal,
    [property: JsonPropertyName("bytesCopied")] long BytesCopied,
    [property: JsonPropertyName("currentFile")] string? CurrentFile,
    [property: JsonPropertyName("speechAnalysis")] string SpeechAnalysis, // not_requested | queued | merged | failed | skipped | unavailable
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("items")] IReadOnlyList<ImportItemStatusDto> Items,
    [property: JsonPropertyName("startedAtUtc")] DateTime StartedAtUtc,
    [property: JsonPropertyName("finishedAtUtc")] DateTime? FinishedAtUtc
);

public record CancelImportResponse(
    [property: JsonPropertyName("canceled")] bool Canceled
);
