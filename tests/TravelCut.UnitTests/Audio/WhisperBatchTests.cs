using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

/// <summary>F14.8/P10: modelo Whisper carregado uma vez por job (lote num único processo do worker).</summary>
public class WhisperBatchTests : IDisposable
{
    private readonly PipelineHarness _h;

    public WhisperBatchTests()
    {
        var whisper = new FakeWhisperService { Available = true, ModelCached = true, FfmpegOnPath = true, BatchSupported = true };
        whisper.OutcomeByPath = path => new TranscriptionOutcome("completed",
            new TranscriptDto(Guid.NewGuid(), Guid.Empty, "large-v3", "pt", "oi " + Path.GetFileName(path), 5.0,
                new List<TranscriptWordDto> { new(0, 0.0, 0.5, "oi", 0.9) }),
            null, "fake", "large-v3");
        _h = new PipelineHarness(whisper, new FakeSilenceService
        {
            FfmpegAvailable = true,
            Outcome = new SilenceDetectionOutcome("completed", new List<VadSilenceSegmentDto>(), null)
        });
    }

    public void Dispose() => _h.Dispose();

    private async Task<PipelineStatusDto> RunAsync(Guid projectId)
    {
        await _h.Manager.StartAsync(new StartPipelineRequest(projectId, null, null, null, false));
        return await _h.WaitForFinishAsync(projectId);
    }

    [Fact]
    public async Task Three_media_load_the_model_once_and_each_gets_its_own_transcript()
    {
        var project = _h.CreateProject();
        var media = new[] { "a", "b", "c" }
            .Select(n => _h.AddMedia(project, $"1-Bruto/{n}.mp4", fingerprint: "fp-" + n))
            .ToList();

        await RunAsync(project.ProjectId);

        Assert.Equal(1, _h.Whisper.LoadModelCalls);
        Assert.Equal(1, _h.Whisper.BatchCalls);
        Assert.Equal(0, _h.Whisper.TranscribeCalls); // nenhuma chamada individual
        foreach (var m in media)
        {
            var analysis = await _h.Manager.GetMediaAnalysisAsync(project.ProjectId, m.MediaId);
            Assert.Equal("completed", analysis!.TranscriptionStatus);
            Assert.Contains(Path.GetFileName(m.RelativePath), analysis.Transcript!.FullText);
        }
    }

    [Fact]
    public async Task Without_batch_support_each_media_loads_the_model_separately()
    {
        _h.Whisper.BatchSupported = false;
        var project = _h.CreateProject();
        foreach (var n in new[] { "a", "b", "c" }) _h.AddMedia(project, $"1-Bruto/{n}.mp4", fingerprint: "fp-" + n);

        await RunAsync(project.ProjectId);

        Assert.Equal(3, _h.Whisper.LoadModelCalls);
        Assert.Equal(3, _h.Whisper.TranscribeCalls);
    }

    [Fact]
    public async Task Cached_media_stay_out_of_the_batch()
    {
        var project = _h.CreateProject();
        var a = _h.AddMedia(project, "1-Bruto/a.mp4", fingerprint: "fp-a");
        _h.AddMedia(project, "1-Bruto/b.mp4", fingerprint: "fp-b");
        _h.AddMedia(project, "1-Bruto/c.mp4", fingerprint: "fp-c");

        await RunAsync(project.ProjectId);
        Assert.Equal(1, _h.Whisper.LoadModelCalls);

        // Segunda rodada: tudo em cache, nenhum processo do worker (nem lote).
        await RunAsync(project.ProjectId);
        Assert.Equal(1, _h.Whisper.LoadModelCalls);
        Assert.Equal(1, _h.Whisper.BatchCalls);
        Assert.NotNull(a);
    }

    [Fact]
    public async Task Failed_item_in_batch_is_recovered_individually_without_hurting_the_others()
    {
        var project = _h.CreateProject();
        foreach (var n in new[] { "a", "b", "c" }) _h.AddMedia(project, $"1-Bruto/{n}.mp4", fingerprint: "fp-" + n);
        var ok = _h.Whisper.OutcomeByPath!;
        int bCalls = 0;
        _h.Whisper.OutcomeByPath = path => Path.GetFileName(path) == "b.mp4" && bCalls++ == 0
            ? new TranscriptionOutcome("failed", null, "processo caiu", null, "large-v3", TranscriptionFailureCodes.Transient)
            : ok(path);

        var status = await RunAsync(project.ProjectId);

        Assert.Equal("completed", status.State);
        Assert.Equal(1, _h.Whisper.TranscribeCalls); // só o item falho foi refeito, mídia a mídia
    }

    // ---- Serviço real: um único processo com N comandos "transcribe" ----

    [Fact]
    public async Task Real_service_runs_one_worker_process_with_all_transcribe_commands()
    {
        string dir = Path.Combine(Path.GetTempPath(), "TcBatch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string worker = Path.Combine(dir, "whisper_worker.py");
            File.WriteAllText(worker, "# fake");
            var files = new[] { "a.wav", "b.wav", "c.wav" }.Select(n => Path.Combine(dir, n)).ToList();
            foreach (var f in files) File.WriteAllBytes(f, new byte[8]);

            string ok = FixtureFiles.Read("whisper_transcribe_ok.json");
            var runner = new FakeProcessRunner(r =>
            {
                if (FakeProcessRunner.IsPythonCheck(r)) return FakeProcessRunner.Completed(0, "TRAVELCUT_PY=3.11.9\n");
                if (FakeProcessRunner.IsCommand(r, "probe"))
                    return FakeProcessRunner.Completed(0, FixtureFiles.Read("whisper_probe_ok.json").Replace("__REQ__", FakeProcessRunner.RequestIdFrom(r)));

                // Um "transcribe" por linha de stdin; o worker responde cada um com o seu requestId.
                var stdout = new System.Text.StringBuilder();
                foreach (var line in r.StandardInput!.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.GetProperty("command").GetString() != "transcribe") continue;
                    stdout.AppendLine(ok.Replace("__REQ__", doc.RootElement.GetProperty("requestId").GetString()!).ReplaceLineEndings(" "));
                }
                return FakeProcessRunner.Completed(0, stdout.ToString());
            });
            var service = new WhisperTranscriptionService(NullLogger<WhisperTranscriptionService>.Instance, runner,
                new WhisperServiceOptions(PythonPath: @"C:\Tools\Python 3.11\python.exe", WorkerScriptPath: worker));
            var items = files.Select(f => new TranscriptionBatchItem(f, Guid.NewGuid())).ToList();

            var tasks = service.TranscribeBatch(items, "large-v3", "pt", CancellationToken.None);

            Assert.NotNull(tasks);
            var outcomes = await Task.WhenAll(tasks!);
            Assert.All(outcomes, o => Assert.Equal("completed", o.Status));
            var transcribeRuns = runner.Requests.Where(r => FakeProcessRunner.IsCommand(r, "transcribe")).ToList();
            Assert.Single(transcribeRuns); // um processo para as 3 mídias
            Assert.Equal(3, transcribeRuns[0].StandardInput!.Split('\n').Count(l => l.Contains("\"command\":\"transcribe\"")));
            Assert.Contains("\"command\":\"exit\"", transcribeRuns[0].StandardInput);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
