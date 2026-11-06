namespace TravelCut.Application.Ports;

// Abstração de subprocesso para FFmpeg/FFprobe e worker Python. Implementações devem:
// - usar ArgumentList (nunca Arguments com interpolação), sem shell;
// - encerrar a árvore de processos em timeout/cancelamento;
// - nunca incluir caminhos completos em mensagens de erro (StartError).
public enum ProcessRunStatus
{
    Completed,
    NotStarted,
    TimedOut,
    Canceled
}

public sealed record ProcessRunRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    string? StandardInput = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    string? WorkingDirectory = null
);

public sealed record ProcessRunResult(
    ProcessRunStatus Status,
    int? ExitCode,                 // preenchido apenas quando Status == Completed
    string StandardOutput,
    string StandardError,
    string? StartError = null      // mensagem curta e sem caminho quando NotStarted
);

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct = default);
}
