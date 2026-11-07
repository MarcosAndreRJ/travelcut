using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

// Processo real (cmd.exe), sem mídia nem rede: confirma a leitura de stdout por linha usada pela telemetria do worker.
public class ProcessRunnerLineStreamingTests
{
    private static ProcessRunRequest Echo(Action<string>? onLine) => new(
        "cmd.exe",
        new[] { "/c", "echo primeira& echo segunda" },
        TimeSpan.FromSeconds(30),
        StandardOutputLine: onLine);

    [Fact]
    public async Task LineCallback_ReceivesEachLine_AndStandardOutputStaysComplete()
    {
        var lines = new List<string>();

        var result = await new ProcessRunner().RunAsync(Echo(lines.Add));

        Assert.Equal(ProcessRunStatus.Completed, result.Status);
        Assert.Equal(2, lines.Count);
        Assert.Contains("primeira", lines[0]);
        Assert.Contains("segunda", lines[1]);
        Assert.Contains("primeira", result.StandardOutput);
        Assert.Contains("segunda", result.StandardOutput);
    }

    [Fact]
    public async Task FailingCallback_DoesNotBreakTheRun()
    {
        var result = await new ProcessRunner().RunAsync(Echo(_ => throw new InvalidOperationException("telemetria")));

        Assert.Equal(ProcessRunStatus.Completed, result.Status);
        Assert.Contains("segunda", result.StandardOutput);
    }
}
