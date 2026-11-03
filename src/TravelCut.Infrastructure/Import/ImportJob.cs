using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Enums;

namespace TravelCut.Infrastructure.Import;

/// <summary>Resultados por item (valores do contrato ImportItemStatusDto.Result).</summary>
internal static class ImportItemResults
{
    public const string Pending = "pending";
    public const string Copying = "copying";
    public const string Imported = "imported";
    public const string Renamed = "renamed";
    public const string RegisteredInPlace = "registered_in_place";
    public const string SkippedDuplicate = "skipped_duplicate";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
}

/// <summary>Estados do job (valores do contrato ImportJobStatusDto.State).</summary>
internal static class ImportJobStates
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string CompletedWithErrors = "completed_with_errors";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
}

internal sealed class ImportItemState
{
    public required string SourcePath { get; init; }
    public required string FileName { get; init; }
    public required long ByteLength { get; init; }
    public required MediaKind Kind { get; init; }
    public required bool InPlace { get; init; }
    public required bool NeedsCopy { get; init; }
    public string Result { get; set; } = ImportItemResults.Pending;
    public string? DestinationRelativePath { get; set; }
    public string? FinalFullPath { get; set; }
    public string? Message { get; set; }

    public bool IsFinal => Result is not (ImportItemResults.Pending or ImportItemResults.Copying);
}

/// <summary>
/// Estado de um job de importação. Todas as leituras/escritas passam por <see cref="Mutate"/> e
/// <see cref="Snapshot"/>, que usam o mesmo lock: o status entregue ao frontend é sempre um snapshot consistente.
/// </summary>
internal sealed class ImportJob
{
    private readonly object _sync = new();
    private readonly List<string> _notes = new();
    private List<ImportItemState> _items = new();
    private string _state = ImportJobStates.Queued;
    private string _stage = "preflight";
    private long _bytesTotal;
    private long _bytesCopied;
    private string? _currentFile;
    private string _speechAnalysis = "not_requested";
    private string? _finalMessage;
    private bool _finished;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private DateTime? _finishedAtUtc;

    public ImportJob(
        Guid jobId,
        Guid projectId,
        string projectRoot,
        string destinationRoot,
        string destinationDirectory,
        string destinationFolder,
        string subfolder,
        IReadOnlyList<string> sourcePaths,
        IReadOnlyList<string> tags,
        bool copyFiles,
        bool generateThumbnails,
        bool analyzeMetadata,
        bool detectSpeech)
    {
        JobId = jobId;
        ProjectId = projectId;
        ProjectRoot = projectRoot;
        DestinationRoot = destinationRoot;
        DestinationDirectory = destinationDirectory;
        DestinationFolder = destinationFolder;
        Subfolder = subfolder;
        SourcePaths = sourcePaths;
        Tags = tags;
        CopyFiles = copyFiles;
        GenerateThumbnails = generateThumbnails;
        AnalyzeMetadata = analyzeMetadata;
        DetectSpeech = detectSpeech;
        _speechAnalysis = detectSpeech ? "unavailable" : "not_requested";
    }

    public Guid JobId { get; }
    public Guid ProjectId { get; }
    public string ProjectRoot { get; }
    public string DestinationRoot { get; }
    public string DestinationDirectory { get; }
    public string DestinationFolder { get; }
    public string Subfolder { get; }
    public IReadOnlyList<string> SourcePaths { get; }
    public IReadOnlyList<string> Tags { get; }
    public bool CopyFiles { get; }
    public bool GenerateThumbnails { get; }
    public bool AnalyzeMetadata { get; }
    public bool DetectSpeech { get; }

    /// <summary>
    /// Não é descartado de propósito: <see cref="CancellationTokenSource.Cancel"/> pode ser chamado a qualquer
    /// momento por <see cref="RequestCancel"/>, e o token precisa permanecer válido até o fim do job.
    /// </summary>
    public CancellationTokenSource Cts { get; } = new();

    public CancellationToken Token => Cts.Token;

    /// <summary>Execução de escrita protegida pelo lock do job.</summary>
    public void Mutate(Action<ImportJob> change)
    {
        lock (_sync)
        {
            change(this);
        }
    }

    /// <summary>Acesso aos itens dentro de Mutate/Snapshot (sem vazamento de referência).</summary>
    internal List<ImportItemState> Items
    {
        get => _items;
        set => _items = value;
    }

    internal string State { get => _state; set => _state = value; }
    internal string Stage { get => _stage; set => _stage = value; }
    internal long BytesTotal { get => _bytesTotal; set => _bytesTotal = value; }
    internal long BytesCopied { get => _bytesCopied; set => _bytesCopied = value; }
    internal string? CurrentFile { get => _currentFile; set => _currentFile = value; }
    internal bool IsFinished => _finished;

    internal void AddNote(string note)
    {
        if (!string.IsNullOrWhiteSpace(note))
            _notes.Add(note);
    }

    /// <summary>Finaliza o job uma única vez. Itens ainda pendentes viram canceled.</summary>
    internal void Finish(string state, string summary)
    {
        if (_finished) return;

        foreach (var item in _items.Where(i => i.Result is ImportItemResults.Pending or ImportItemResults.Copying))
        {
            item.Result = ImportItemResults.Canceled;
            item.Message ??= "Não processado: a importação foi interrompida.";
        }

        _state = state;
        // Falha mantém o estágio em que parou (ex.: "preflight"); demais finais marcam "done".
        if (state != ImportJobStates.Failed)
        {
            _stage = "done";
        }
        _currentFile = null;
        _finalMessage = string.Join(" ", new[] { summary }.Concat(_notes).Where(s => !string.IsNullOrWhiteSpace(s)));
        _finishedAtUtc = DateTime.UtcNow;
        _finished = true;
    }

    /// <summary>Solicita cancelamento. Retorna false se o job já terminou.</summary>
    public bool RequestCancel()
    {
        lock (_sync)
        {
            if (_finished) return false;
            Cts.Cancel();
            return true;
        }
    }

    public ImportJobStatusDto Snapshot()
    {
        lock (_sync)
        {
            var items = _items
                .Select(i => new ImportItemStatusDto(i.SourcePath, i.Result, i.DestinationRelativePath, i.Message))
                .ToList();

            int processed = _items.Count(i => i.IsFinal);
            string? message = _finished
                ? _finalMessage
                : (_notes.Count > 0 ? string.Join(" ", _notes) : null);

            return new ImportJobStatusDto(
                JobId: JobId,
                ProjectId: ProjectId,
                State: _state,
                Stage: _stage,
                TotalFiles: _items.Count,
                ProcessedFiles: processed,
                BytesTotal: _bytesTotal,
                BytesCopied: _bytesCopied,
                CurrentFile: _currentFile,
                SpeechAnalysis: _speechAnalysis,
                Message: message,
                Items: items,
                StartedAtUtc: _startedAtUtc,
                FinishedAtUtc: _finishedAtUtc);
        }
    }
}
