using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using TravelCut.Application.Ports;

namespace TravelCut.Infrastructure.Media.Preview;

/// <summary>
/// Variante de <see cref="TravelCut.Infrastructure.Audio.ProcessRunner"/> para stdout binário.
/// Motivo: ProcessRunner lê stdout como texto UTF-8 (inadequado para PCM). Aqui o stdout é lido pelo
/// <see cref="Stream"/> bruto (BaseStream) no callback do chamador, sem materializar tudo em memória.
/// Garantias iguais: ArgumentList sem shell, stdin fechado, stderr capturado em paralelo,
/// Kill(entireProcessTree: true) em timeout/cancelamento, mensagens sem caminhos.
/// </summary>
public sealed class BinaryStdoutProcessRunner : IBinaryStdoutProcessRunner
{
    private static readonly TimeSpan KillGrace = TimeSpan.FromSeconds(5);

    public async Task<BinaryStdoutRunResult> RunAsync(
        ProcessRunRequest request,
        Func<Stream, CancellationToken, Task> readStdout,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentNullException.ThrowIfNull(request.Arguments);
        ArgumentNullException.ThrowIfNull(readStdout);

        if (ct.IsCancellationRequested)
        {
            return new BinaryStdoutRunResult(ProcessRunStatus.Canceled, null, string.Empty);
        }

        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardErrorEncoding = new UTF8Encoding(false)
        };

        foreach (var arg in request.Arguments)
        {
            psi.ArgumentList.Add(arg);
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

        // Nenhuma entrada é esperada: fecha stdin para o processo não ficar aguardando.
        try { process.StandardInput.Close(); } catch (IOException) { }

        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeoutCts = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            // BaseStream: bytes crus, sem decodificação de texto.
            await readStdout(process.StandardOutput.BaseStream, linked.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var status = ct.IsCancellationRequested ? ProcessRunStatus.Canceled : ProcessRunStatus.TimedOut;
            KillTree(process);
            string stderr = await CollectAfterKillAsync(stderrTask).ConfigureAwait(false);
            return new BinaryStdoutRunResult(status, null, stderr);
        }
        catch
        {
            // Erro de leitura/consumo: encerra a árvore antes de propagar.
            KillTree(process);
            throw;
        }

        string errText = await stderrTask.ConfigureAwait(false);
        return new BinaryStdoutRunResult(ProcessRunStatus.Completed, process.ExitCode, errText);
    }

    private static BinaryStdoutRunResult NotStarted() =>
        new(ProcessRunStatus.NotStarted, null, string.Empty,
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

    private static async Task<string> CollectAfterKillAsync(Task<string> stderrTask)
    {
        // Após Kill o pipe fecha; o limite evita pendurar se um neto segurar o handle.
        await Task.WhenAny(stderrTask, Task.Delay(KillGrace)).ConfigureAwait(false);
        return stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
    }
}
