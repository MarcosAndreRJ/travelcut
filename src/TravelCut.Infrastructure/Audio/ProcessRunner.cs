using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using TravelCut.Application.Ports;

namespace TravelCut.Infrastructure.Audio;

// Implementação real de IProcessRunner: ProcessStartInfo.ArgumentList, sem shell,
// sem janela, stdin fechado logo após a escrita, stdout/stderr capturados e
// Kill(entireProcessTree: true) em timeout ou cancelamento.
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan KillGrace = TimeSpan.FromSeconds(5);

    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentNullException.ThrowIfNull(request.Arguments);

        if (ct.IsCancellationRequested)
        {
            return new ProcessRunResult(ProcessRunStatus.Canceled, null, string.Empty, string.Empty);
        }

        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom
        };

        foreach (var arg in request.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory) && Directory.Exists(request.WorkingDirectory))
        {
            psi.WorkingDirectory = request.WorkingDirectory;
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach (var kv in request.EnvironmentVariables)
            {
                psi.Environment[kv.Key] = kv.Value;
            }
        }

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start())
            {
                return NotStarted();
            }
        }
        catch (Win32Exception)
        {
            return NotStarted();
        }
        catch (InvalidOperationException)
        {
            return NotStarted();
        }

        var stdoutTask = request.StandardOutputLine is { } onLine
            ? ReadLinesAsync(process.StandardOutput, onLine)
            : process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.StandardInput.WriteAsync(request.StandardInput ?? string.Empty).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Processo encerrou antes de ler stdin; a saída/código de retorno decidem o resultado.
        }
        finally
        {
            try { process.StandardInput.Close(); } catch (IOException) { }
        }

        using var timeoutCts = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var status = ct.IsCancellationRequested ? ProcessRunStatus.Canceled : ProcessRunStatus.TimedOut;
            KillTree(process);
            var (stdout, stderr) = await CollectAfterKillAsync(stdoutTask, stderrTask).ConfigureAwait(false);
            return new ProcessRunResult(status, null, stdout, stderr);
        }

        var outText = await stdoutTask.ConfigureAwait(false);
        var errText = await stderrTask.ConfigureAwait(false);
        return new ProcessRunResult(ProcessRunStatus.Completed, process.ExitCode, outText, errText);
    }

    private static async Task<string> ReadLinesAsync(StreamReader reader, Action<string> onLine)
    {
        var text = new System.Text.StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            text.Append(line).Append('\n');
            try
            {
                onLine(line);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Telemetria não pode interromper a leitura nem o processo.
            }
        }
        return text.ToString();
    }

    private static ProcessRunResult NotStarted() =>
        new(ProcessRunStatus.NotStarted, null, string.Empty, string.Empty,
            "Não foi possível iniciar o processo externo (executável ausente ou inválido).");

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private static async Task<(string Stdout, string Stderr)> CollectAfterKillAsync(
        Task<string> stdoutTask,
        Task<string> stderrTask)
    {
        // Após Kill os pipes fecham; o limite evita pendurar se um neto segurar o handle.
        var all = Task.WhenAll(stdoutTask, stderrTask);
        await Task.WhenAny(all, Task.Delay(KillGrace)).ConfigureAwait(false);
        var stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
        var stderr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
        return (stdout, stderr);
    }
}
