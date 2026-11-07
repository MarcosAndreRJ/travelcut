using System.Text.RegularExpressions;
using TravelCut.Application.Audio;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Analysis;

/// <summary>
/// Registro auditável de uma execução do worker. Logs em memória limitados: mantém as últimas linhas e conta as descartadas.
/// </summary>
public sealed partial class WhisperExecutionRecord : IWhisperExecutionObserver
{
    public const int MaxLogLines = 200;
    public const int MaxLogLineLength = 500;

    private readonly object _sync = new();
    private readonly Queue<WhisperLogLineDto> _logs = new();
    private readonly Guid _jobId;
    private readonly Guid _mediaId;
    private readonly Func<DateTime> _utcNow;

    private WhisperExecutionInfo? _info;
    private DateTime? _startedAtUtc;
    private DateTime? _finishedAtUtc;
    private WhisperExecutionResult? _result;
    private int _dropped;

    public WhisperExecutionRecord(Guid jobId, Guid mediaId, Func<DateTime>? utcNow = null)
    {
        _jobId = jobId;
        _mediaId = mediaId;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public void OnStarted(WhisperExecutionInfo info)
    {
        lock (_sync)
        {
            _info = info;
            _startedAtUtc = _utcNow();
        }
    }

    public void OnLog(string stream, string text)
    {
        var clean = Sanitize(text);
        if (clean.Length == 0) return;

        lock (_sync)
        {
            _logs.Enqueue(new WhisperLogLineDto(_utcNow(), stream, clean));
            if (_logs.Count > MaxLogLines)
            {
                _logs.Dequeue();
                _dropped++;
            }
        }
    }

    public void OnFinished(WhisperExecutionResult result)
    {
        lock (_sync)
        {
            _result = result;
            _finishedAtUtc = _utcNow();
        }
    }

    /// <summary>null enquanto o processo não foi montado (ex.: Python ausente: nada foi executado).</summary>
    public WhisperExecutionDto? ToDto()
    {
        lock (_sync)
        {
            if (_info is null) return null;
            return new WhisperExecutionDto(
                JobId: _jobId,
                MediaId: _mediaId,
                RequestId: _info.RequestId,
                MediaPath: _info.MediaPath,
                StartedAtUtc: _startedAtUtc,
                FinishedAtUtc: _finishedAtUtc,
                PythonExecutable: _info.PythonExecutable,
                Arguments: _info.Arguments.ToList(),
                WorkerScript: _info.WorkerScript,
                WorkingDirectory: _info.WorkingDirectory,
                EnvironmentVariables: new Dictionary<string, string>(_info.EnvironmentVariables),
                StandardInput: _info.StandardInput,
                Model: _info.Model,
                Language: _info.Language,
                Device: _info.Device,
                ProcessState: _result?.ProcessState ?? "running",
                ExitCode: _result?.ExitCode,
                Canceled: _result?.ProcessState == "canceled",
                Error: _result?.Error,
                LogLines: _logs.ToList(),
                DroppedLogLines: _dropped,
                DiagnosticScript: WhisperDiagnosticScript.Build(_info));
        }
    }

    /// <summary>Corta linhas longas e oculta valores que parecem credenciais.</summary>
    public static string Sanitize(string text)
    {
        var line = SecretPattern().Replace(text.Trim(), "$1=[oculto]");
        return line.Length <= MaxLogLineLength ? line : line[..MaxLogLineLength] + "…";
    }

    [GeneratedRegex(@"(?i)\b(token|password|passwd|secret|api[_-]?key|authorization)\b\s*[=:]\s*\S+")]
    private static partial Regex SecretPattern();
}
