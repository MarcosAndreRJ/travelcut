using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

// Fake de subprocesso: registra requisições e responde por função. Sem executáveis reais.
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<ProcessRunRequest, ProcessRunResult> _handler;

    public FakeProcessRunner(Func<ProcessRunRequest, ProcessRunResult> handler) => _handler = handler;

    public List<ProcessRunRequest> Requests { get; } = new();

    public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(new ProcessRunResult(ProcessRunStatus.Canceled, null, string.Empty, string.Empty));
        }

        return Task.FromResult(_handler(request));
    }

    public static ProcessRunResult NotStarted() =>
        new(ProcessRunStatus.NotStarted, null, string.Empty, string.Empty, "Não foi possível iniciar o processo externo.");

    public static ProcessRunResult Completed(int exitCode, string stdout = "", string stderr = "") =>
        new(ProcessRunStatus.Completed, exitCode, stdout, stderr);

    public static ProcessRunResult Status(ProcessRunStatus status) =>
        new(status, null, string.Empty, string.Empty);

    public static bool IsPythonCheck(ProcessRunRequest r) =>
        r.Arguments.Count >= 2 && r.Arguments[^2] == "-c";

    public static bool IsCommand(ProcessRunRequest r, string command) =>
        r.StandardInput is not null && r.StandardInput.Contains($"\"command\":\"{command}\"", StringComparison.Ordinal);

    public static string RequestIdFrom(ProcessRunRequest r)
    {
        var firstLine = (r.StandardInput ?? string.Empty).Split('\n')[0];
        using var doc = JsonDocument.Parse(firstLine);
        return doc.RootElement.GetProperty("requestId").GetString()!;
    }
}

// Leitura de fixtures ao lado deste arquivo (sem depender de cópia no bin).
internal static class FixtureFiles
{
    public static string Read(string name, [CallerFilePath] string callerPath = "")
    {
        var dir = Path.GetDirectoryName(callerPath)!;
        return File.ReadAllText(Path.Combine(dir, "Fixtures", name));
    }
}

public class WhisperTranscriptionServiceTests
{
    private const string PythonPath = @"C:\Tools\Python 3.11\python.exe";
    private const string PythonVersionOutput = "TRAVELCUT_PY=3.11.9\n";

    private static (string Root, string Script) CreateWorkerScript()
    {
        // Diretório com espaço para exercitar o caminho do script.
        var root = Path.Combine(Path.GetTempPath(), "TravelCut Tests " + Guid.NewGuid().ToString("N"), "workers");
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "whisper_worker.py");
        File.WriteAllText(script, "# stub de teste; não executado\n");
        return (root, script);
    }

    private static string CreateMediaFile(out string root)
    {
        // Nome com espaços, apóstrofo e cerquilha (aspas duplas não são permitidas no Windows).
        root = Path.Combine(Path.GetTempPath(), "TravelCut Tests " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var media = Path.Combine(root, "clip 'com' #1 & espaço.mp4");
        File.WriteAllText(media, "fixture-bytes-not-decoded");
        return media;
    }

    private static Func<ProcessRunRequest, ProcessRunResult> PythonOk(
        string? transcribeResponse = null,
        string probeResponse = "whisper_probe_ok.json")
    {
        return r =>
        {
            if (FakeProcessRunner.IsPythonCheck(r)) return FakeProcessRunner.Completed(0, PythonVersionOutput);
            if (FakeProcessRunner.IsCommand(r, "probe"))
                return FakeProcessRunner.Completed(0, FixtureFiles.Read(probeResponse).Replace("__REQ__", FakeProcessRunner.RequestIdFrom(r)));
            if (FakeProcessRunner.IsCommand(r, "transcribe") && transcribeResponse is not null)
                return FakeProcessRunner.Completed(0, transcribeResponse.Replace("__REQ__", FakeProcessRunner.RequestIdFrom(r)));
            return FakeProcessRunner.NotStarted();
        };
    }

    private static WhisperTranscriptionService CreateService(FakeProcessRunner runner, string script, string? python = PythonPath) =>
        new(NullLogger<WhisperTranscriptionService>.Instance, runner,
            new WhisperServiceOptions(PythonPath: python, WorkerScriptPath: script));

    [Fact]
    public async Task PythonNotFound_IsUnavailable_AndNeverSendsTranscribe()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        var runner = new FakeProcessRunner(_ => FakeProcessRunner.NotStarted());
        var service = new WhisperTranscriptionService(NullLogger<WhisperTranscriptionService>.Instance, runner,
            new WhisperServiceOptions(PythonPath: null, WorkerScriptPath: script));

        var probe = await service.ProbeEnvironmentAsync();
        Assert.False(probe.HasPython);
        Assert.False(probe.HasWhisper);
        Assert.Null(probe.Engine);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid());

        Assert.Equal("unavailable", outcome.Status);
        Assert.Null(outcome.Transcript);
        Assert.Null(outcome.Engine);
        Assert.Equal("large-v3", outcome.ModelId);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Reason));
        Assert.DoesNotContain(runner.Requests, r => FakeProcessRunner.IsCommand(r, "transcribe"));
    }

    [Fact]
    public async Task MicrosoftStoreAlias_ExitCode9009_IsTreatedAsPythonMissing()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        // Alias da Store: não é Python real; sai com 9009 e mensagem própria.
        var runner = new FakeProcessRunner(r => FakeProcessRunner.IsPythonCheck(r)
            ? FakeProcessRunner.Completed(9009, string.Empty,
                "Python não foi encontrado; execute sem argumentos ou instale o Python na Microsoft Store.")
            : FakeProcessRunner.NotStarted());
        var service = CreateService(runner, script, python: null);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid());

        Assert.Equal("unavailable", outcome.Status);
        Assert.Null(outcome.Transcript);
        Assert.DoesNotContain(runner.Requests, r => FakeProcessRunner.IsCommand(r, "transcribe"));
    }

    [Fact]
    public async Task PythonWithoutMarker_ExitZero_IsRejected()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        // Launcher que responde 0 mas sem a assinatura de Python real.
        var runner = new FakeProcessRunner(r => FakeProcessRunner.IsPythonCheck(r)
            ? FakeProcessRunner.Completed(0, "Python was not found; run without arguments")
            : FakeProcessRunner.NotStarted());
        var service = CreateService(runner, script, python: null);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid());

        Assert.Equal("unavailable", outcome.Status);
    }

    [Fact]
    public async Task WorkerScriptMissing_IsUnavailable()
    {
        var media = CreateMediaFile(out _);
        var runner = new FakeProcessRunner(PythonOk());
        var service = new WhisperTranscriptionService(NullLogger<WhisperTranscriptionService>.Instance, runner,
            new WhisperServiceOptions(PythonPath: PythonPath,
                WorkerScriptPath: Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid().ToString("N"), "whisper_worker.py")));

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid());

        Assert.Equal("unavailable", outcome.Status);
        Assert.Null(outcome.Transcript);
    }

    [Fact]
    public async Task OpenAiWhisperNotInstalled_IsUnavailable()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        const string probe = "{\"protocol\":\"travelcut.whisper\",\"version\":1,\"requestId\":\"__REQ__\",\"ok\":true," +
                             "\"result\":{\"python\":\"3.11.9\",\"whisperInstalled\":false,\"whisperVersion\":null," +
                             "\"cuda\":false,\"device\":\"cpu\",\"ffmpegOnPath\":false,\"defaultModel\":\"large-v3\",\"modelCached\":false}}";
        var runner = new FakeProcessRunner(r =>
        {
            if (FakeProcessRunner.IsPythonCheck(r)) return FakeProcessRunner.Completed(0, PythonVersionOutput);
            if (FakeProcessRunner.IsCommand(r, "probe")) return FakeProcessRunner.Completed(0, probe.Replace("__REQ__", FakeProcessRunner.RequestIdFrom(r)));
            return FakeProcessRunner.NotStarted();
        });
        var service = CreateService(runner, script);

        var probeResult = await service.ProbeEnvironmentAsync();
        Assert.True(probeResult.HasPython);
        Assert.False(probeResult.HasWhisper);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid());
        Assert.Equal("unavailable", outcome.Status);
        Assert.DoesNotContain(runner.Requests, r => FakeProcessRunner.IsCommand(r, "transcribe"));
    }

    [Fact]
    public async Task ModelNotAvailable_IsUnavailable_AndNeverSubstitutesModel()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        var runner = new FakeProcessRunner(PythonOk(FixtureFiles.Read("whisper_model_not_available.json")));
        var service = CreateService(runner, script);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid(), model: "large-v3");

        Assert.Equal("unavailable", outcome.Status);
        Assert.Null(outcome.Transcript);
        Assert.Equal("large-v3", outcome.ModelId);
        Assert.Contains("large-v3", outcome.Reason);

        var transcribeRequests = runner.Requests.Where(r => FakeProcessRunner.IsCommand(r, "transcribe")).ToList();
        Assert.Single(transcribeRequests);
        Assert.Contains("\"model\":\"large-v3\"", transcribeRequests[0].StandardInput);
    }

    [Fact]
    public async Task ModelNotAvailable_ForOtherRequestedModel_StillPassesRequestedModelOnly()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        var runner = new FakeProcessRunner(PythonOk(FixtureFiles.Read("whisper_model_not_available.json")));
        var service = CreateService(runner, script);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid(), model: "medium");

        Assert.Equal("unavailable", outcome.Status);
        var sent = runner.Requests.Single(r => FakeProcessRunner.IsCommand(r, "transcribe"));
        Assert.Contains("\"model\":\"medium\"", sent.StandardInput);
        Assert.DoesNotContain("large-v3", sent.StandardInput);
    }

    [Fact]
    public async Task ValidFixture_IsCompleted_WithMappedWords()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        var mediaId = Guid.NewGuid();
        var runner = new FakeProcessRunner(PythonOk(FixtureFiles.Read("whisper_transcribe_ok.json")));
        var service = CreateService(runner, script);

        var outcome = await service.TranscribeAsync(media, mediaId);

        Assert.Equal("completed", outcome.Status);
        Assert.Null(outcome.Reason);
        Assert.Equal("openai-whisper 20240930", outcome.Engine);
        Assert.Equal("large-v3", outcome.ModelId);
        Assert.NotNull(outcome.Transcript);

        var t = outcome.Transcript!;
        Assert.Equal(mediaId, t.MediaId);
        Assert.Equal("large-v3", t.ModelId);
        Assert.Equal("vamos pegar a estrada agora", t.FullText);
        Assert.Equal(10.5, t.AudioDurationSeconds);
        Assert.Equal(5, t.Words.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, t.Words.Select(w => w.Seq).ToArray());

        var first = t.Words[0];
        Assert.Equal("vamos", first.Text);
        Assert.Equal(0.12, first.StartSeconds);
        Assert.Equal(0.48, first.EndSeconds);
        Assert.Equal(0.9312, first.Confidence); // probability real do Whisper

        var transcribe = runner.Requests.Single(r => FakeProcessRunner.IsCommand(r, "transcribe"));
        Assert.Contains("\"language\":\"pt\"", transcribe.StandardInput);
    }

    [Fact]
    public async Task EmptyTranscript_WithValidAudio_IsCompletedWithZeroWords()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        var runner = new FakeProcessRunner(PythonOk(FixtureFiles.Read("whisper_transcribe_empty.json")));
        var service = CreateService(runner, script);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid());

        Assert.Equal("completed", outcome.Status);
        Assert.NotNull(outcome.Transcript);
        Assert.Empty(outcome.Transcript!.Words);
        Assert.Equal(7.0, outcome.Transcript.AudioDurationSeconds);
    }

    [Fact]
    public async Task TranscribeTimeout_IsFailed()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        var runner = new FakeProcessRunner(r =>
            FakeProcessRunner.IsPythonCheck(r) ? FakeProcessRunner.Completed(0, PythonVersionOutput)
            : FakeProcessRunner.IsCommand(r, "probe") ? FakeProcessRunner.Completed(0, FixtureFiles.Read("whisper_probe_ok.json").Replace("__REQ__", FakeProcessRunner.RequestIdFrom(r)))
            : FakeProcessRunner.Status(ProcessRunStatus.TimedOut));
        var service = CreateService(runner, script);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid());

        Assert.Equal("failed", outcome.Status);
        Assert.Null(outcome.Transcript);
        Assert.Contains("Tempo limite", outcome.Reason);
    }

    [Fact]
    public async Task TranscribeProcessCanceled_IsCanceled()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        var runner = new FakeProcessRunner(r =>
            FakeProcessRunner.IsPythonCheck(r) ? FakeProcessRunner.Completed(0, PythonVersionOutput)
            : FakeProcessRunner.IsCommand(r, "probe") ? FakeProcessRunner.Completed(0, FixtureFiles.Read("whisper_probe_ok.json").Replace("__REQ__", FakeProcessRunner.RequestIdFrom(r)))
            : FakeProcessRunner.Status(ProcessRunStatus.Canceled));
        var service = CreateService(runner, script);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid());

        Assert.Equal("canceled", outcome.Status);
        Assert.Null(outcome.Transcript);
    }

    [Fact]
    public async Task CancellationTokenAlreadyCanceled_IsCanceled_WithoutTranscribe()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        var runner = new FakeProcessRunner(PythonOk(FixtureFiles.Read("whisper_transcribe_ok.json")));
        var service = CreateService(runner, script);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid(), ct: cts.Token);

        Assert.Equal("canceled", outcome.Status);
        Assert.DoesNotContain(runner.Requests, r => FakeProcessRunner.IsCommand(r, "transcribe"));
    }

    [Fact]
    public async Task NonZeroExitWithoutProtocolLine_IsFailed_AndDoesNotEchoStderrPaths()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        var runner = new FakeProcessRunner(r =>
            FakeProcessRunner.IsPythonCheck(r) ? FakeProcessRunner.Completed(0, PythonVersionOutput)
            : FakeProcessRunner.IsCommand(r, "probe") ? FakeProcessRunner.Completed(0, FixtureFiles.Read("whisper_probe_ok.json").Replace("__REQ__", FakeProcessRunner.RequestIdFrom(r)))
            : FakeProcessRunner.Completed(1, "", "Traceback: C:\\Users\\privado\\AppData\\boom"));
        var service = CreateService(runner, script);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid());

        Assert.Equal("failed", outcome.Status);
        Assert.Contains("código 1", outcome.Reason);
        Assert.DoesNotContain("privado", outcome.Reason);
    }

    [Fact]
    public async Task WorkerReturnsDifferentModel_IsFailed_NoSubstitution()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        var swapped = FixtureFiles.Read("whisper_transcribe_ok.json").Replace("\"model\":\"large-v3\"", "\"model\":\"base\"");
        var runner = new FakeProcessRunner(PythonOk(swapped));
        var service = CreateService(runner, script);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid());

        Assert.Equal("failed", outcome.Status);
        Assert.Null(outcome.Transcript);
    }

    [Fact]
    public async Task ProtocolMismatch_IsFailed()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        var wrong = FixtureFiles.Read("whisper_transcribe_ok.json").Replace("\"protocol\":\"travelcut.whisper\"", "\"protocol\":\"outro\"");
        var runner = new FakeProcessRunner(PythonOk(wrong));
        var service = CreateService(runner, script);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid());

        Assert.Equal("failed", outcome.Status);
    }

    [Fact]
    public async Task NonMonotonicWords_BeyondTolerance_IsFailed()
    {
        var (_, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        const string bad = "{\"protocol\":\"travelcut.whisper\",\"version\":1,\"requestId\":\"__REQ__\",\"ok\":true," +
                           "\"result\":{\"text\":\"a b\",\"audio_duration_secs\":10.0,\"words\":[" +
                           "{\"text\":\"a\",\"start\":3.0,\"end\":3.2,\"probability\":0.9}," +
                           "{\"text\":\"b\",\"start\":1.0,\"end\":1.2,\"probability\":0.9}]," +
                           "\"engine\":\"openai-whisper 20240930\",\"model\":\"large-v3\",\"language\":\"pt\"}}";
        var runner = new FakeProcessRunner(PythonOk(bad));
        var service = CreateService(runner, script);

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid());

        Assert.Equal("failed", outcome.Status);
        Assert.Null(outcome.Transcript);
    }

    [Fact]
    public async Task ArgumentList_KeepsPathsAsSingleItems_AndExactPythonFileName()
    {
        var (root, script) = CreateWorkerScript();
        var media = CreateMediaFile(out _);
        var runner = new FakeProcessRunner(PythonOk(FixtureFiles.Read("whisper_transcribe_ok.json")));
        var service = CreateService(runner, script);

        await service.TranscribeAsync(media, Guid.NewGuid());

        var transcribe = runner.Requests.Single(r => FakeProcessRunner.IsCommand(r, "transcribe"));
        Assert.Equal(PythonPath, transcribe.FileName);
        Assert.Single(transcribe.Arguments); // python explícito, sem prefixo (ex.: py -3)
        Assert.Equal(script, transcribe.Arguments[^1]);
        Assert.Contains(root, transcribe.Arguments[^1]);
        Assert.Equal(Path.GetFullPath(script), transcribe.Arguments[^1]);
    }

    [Fact]
    public async Task Probe_IsCached_SecondCallDoesNotSpawnAgain()
    {
        var (_, script) = CreateWorkerScript();
        var runner = new FakeProcessRunner(PythonOk());
        var service = CreateService(runner, script);

        var first = await service.ProbeEnvironmentAsync();
        var second = await service.ProbeEnvironmentAsync();

        Assert.True(first.HasPython);
        Assert.True(first.HasWhisper);
        Assert.Equal("openai-whisper 20240930", first.Engine);
        Assert.Equal(first, second);
        Assert.Equal(1, runner.Requests.Count(FakeProcessRunner.IsPythonCheck));
        Assert.Equal(1, runner.Requests.Count(r => FakeProcessRunner.IsCommand(r, "probe")));
    }

    [Fact]
    public async Task MissingMediaFile_IsFailed_WithoutCallingWorker()
    {
        var (_, script) = CreateWorkerScript();
        var runner = new FakeProcessRunner(PythonOk(FixtureFiles.Read("whisper_transcribe_ok.json")));
        var service = CreateService(runner, script);

        var outcome = await service.TranscribeAsync(Path.Combine(Path.GetTempPath(), "nao-existe.mp4"), Guid.NewGuid());

        Assert.Equal("failed", outcome.Status);
        Assert.Empty(runner.Requests);
    }
}
