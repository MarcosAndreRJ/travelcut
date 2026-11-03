using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Media.Preview;
using Xunit;

namespace TravelCut.UnitTests.Preview;

/// <summary>
/// Testes com processos reais do Windows (cmd.exe, ping) — não usam FFmpeg. Validam que o stdout
/// binário chega intacto ao consumidor, código de saída, timeout e executável ausente.
/// </summary>
public class BinaryStdoutProcessRunnerTests
{
    private static readonly BinaryStdoutProcessRunner Runner = new();

    [Fact]
    public async Task BinaryStdout_IsDeliveredByteForByte()
    {
        // cmd "type" para ao encontrar 0x1A (Ctrl+Z): o padrão de bytes evita esse valor.
        var bytes = Enumerable.Range(0, 1024).Select(i => (byte)((i * 37) % 256)).Where(b => b != 0x1A).ToArray();
        string file = Path.Combine(Path.GetTempPath(), "tc-binary-" + Guid.NewGuid().ToString("N") + ".bin");
        await File.WriteAllBytesAsync(file, bytes);

        try
        {
            var collected = new MemoryStream();
            var result = await Runner.RunAsync(
                new ProcessRunRequest("cmd.exe", new[] { "/c", "type", file }, TimeSpan.FromSeconds(30)),
                async (stream, ct) => await stream.CopyToAsync(collected, ct),
                CancellationToken.None);

            Assert.Equal(ProcessRunStatus.Completed, result.Status);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(bytes, collected.ToArray());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task NonZeroExit_IsReportedWithCode()
    {
        var result = await Runner.RunAsync(
            new ProcessRunRequest("cmd.exe", new[] { "/c", "exit", "3" }, TimeSpan.FromSeconds(30)),
            (stream, ct) => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(ProcessRunStatus.Completed, result.Status);
        Assert.Equal(3, result.ExitCode);
    }

    [Fact]
    public async Task MissingExecutable_IsNotStarted()
    {
        var result = await Runner.RunAsync(
            new ProcessRunRequest(@"C:\tc-inexistente\nao-existe.exe", Array.Empty<string>(), TimeSpan.FromSeconds(5)),
            (stream, ct) => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(ProcessRunStatus.NotStarted, result.Status);
        Assert.Null(result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StartError));
    }

    [Fact]
    public async Task Timeout_KillsProcessAndReportsTimedOut()
    {
        var result = await Runner.RunAsync(
            new ProcessRunRequest("cmd.exe", new[] { "/c", "ping", "-n", "30", "127.0.0.1" }, TimeSpan.FromMilliseconds(700)),
            async (stream, ct) => await stream.CopyToAsync(Stream.Null, ct),
            CancellationToken.None);

        Assert.Equal(ProcessRunStatus.TimedOut, result.Status);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task Cancellation_ReportsCanceled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var result = await Runner.RunAsync(
            new ProcessRunRequest("cmd.exe", new[] { "/c", "ping", "-n", "30", "127.0.0.1" }, TimeSpan.FromSeconds(60)),
            async (stream, ct) => await stream.CopyToAsync(Stream.Null, ct),
            cts.Token);

        Assert.Equal(ProcessRunStatus.Canceled, result.Status);
    }
}
