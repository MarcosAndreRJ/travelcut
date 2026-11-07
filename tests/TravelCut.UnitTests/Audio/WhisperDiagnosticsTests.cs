using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Analysis;
using TravelCut.Infrastructure.Audio;
using Xunit;

namespace TravelCut.UnitTests.Audio;

// F13.3-B: execução auditável do worker. Processos simulados; o único processo real é o PowerShell com um worker falso.
public class WhisperDiagnosticsTests
{
    private sealed class CapturingObserver : IWhisperExecutionObserver
    {
        public WhisperExecutionInfo? Info { get; private set; }
        public WhisperExecutionResult? Result { get; private set; }
        public List<(string Stream, string Text)> Logs { get; } = new();
        public void OnStarted(WhisperExecutionInfo info) => Info = info;
        public void OnLog(string stream, string text) => Logs.Add((stream, text));
        public void OnFinished(WhisperExecutionResult result) => Result = result;
    }

    private const string PythonPath = @"C:\Ferramentas Python\Python 3.11\python.exe";

    private static (string Script, string Media) Fixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "TravelCut Diag ç " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "workers"));
        var script = Path.Combine(root, "workers", "whisper_worker.py");
        File.WriteAllText(script, "# stub de teste; não executado\n");
        var media = Path.Combine(root, "Viagem São Tomé", "clipe ação 'praia' 01.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(media)!);
        File.WriteAllText(media, "fixture-bytes-not-decoded");
        return (script, media);
    }

    private static Func<ProcessRunRequest, ProcessRunResult> Worker(Func<ProcessRunRequest, string, ProcessRunResult> transcribe) => r =>
    {
        if (FakeProcessRunner.IsPythonCheck(r)) return FakeProcessRunner.Completed(0, "TRAVELCUT_PY=3.11.9\n");
        if (FakeProcessRunner.IsCommand(r, "probe"))
            return FakeProcessRunner.Completed(0, FixtureFiles.Read("whisper_probe_ok.json").Replace("__REQ__", FakeProcessRunner.RequestIdFrom(r)));
        if (FakeProcessRunner.IsCommand(r, "transcribe")) return transcribe(r, FakeProcessRunner.RequestIdFrom(r));
        return FakeProcessRunner.NotStarted();
    };

    private static WhisperTranscriptionService Service(FakeProcessRunner runner, string script) =>
        new(NullLogger<WhisperTranscriptionService>.Instance, runner,
            new WhisperServiceOptions(PythonPath: PythonPath, WorkerScriptPath: script));

    [Fact]
    public async Task Observer_ReceivesTheExactExecutable_ArgumentsAndStdinSentToTheProcess()
    {
        var (script, media) = Fixture();
        var runner = new FakeProcessRunner(Worker((r, req) =>
            FakeProcessRunner.Completed(0, FixtureFiles.Read("whisper_transcribe_ok.json").Replace("__REQ__", req))));
        var observer = new CapturingObserver();

        var outcome = await Service(runner, script).TranscribeAsync(media, Guid.NewGuid(), execution: observer);

        var sent = runner.Requests.Single(r => FakeProcessRunner.IsCommand(r, "transcribe"));
        var info = observer.Info!;
        Assert.Equal(sent.FileName, info.PythonExecutable);
        Assert.Equal(sent.Arguments, info.Arguments);
        Assert.Equal(sent.StandardInput, info.StandardInput);
        Assert.Equal(script, info.WorkerScript);
        Assert.Equal(Path.GetFullPath(media), info.MediaPath);
        Assert.Equal("large-v3", info.Model);
        Assert.Equal("pt", info.Language);
        Assert.Equal("1", info.EnvironmentVariables["PYTHONUTF8"]);
        Assert.Equal("completed", outcome.Status);
        Assert.Equal("completed", observer.Result!.ProcessState);
        Assert.Equal(0, observer.Result.ExitCode);
    }

    [Fact]
    public async Task Stdin_IsValidNdjson_WithMediaPathEscaped_AndEndsWithExit()
    {
        var (script, media) = Fixture();
        var runner = new FakeProcessRunner(Worker((r, req) =>
            FakeProcessRunner.Completed(0, FixtureFiles.Read("whisper_transcribe_ok.json").Replace("__REQ__", req))));
        var observer = new CapturingObserver();

        await Service(runner, script).TranscribeAsync(media, Guid.NewGuid(), execution: observer);

        var lines = observer.Info!.StandardInput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        using var request = System.Text.Json.JsonDocument.Parse(lines[0]);
        Assert.Equal("transcribe", request.RootElement.GetProperty("command").GetString());
        Assert.Equal(Path.GetFullPath(media), request.RootElement.GetProperty("payload").GetProperty("mediaPath").GetString());
        Assert.Equal("exit", System.Text.Json.JsonDocument.Parse(lines[1]).RootElement.GetProperty("command").GetString());
        Assert.All(observer.Info.StandardInput, c => Assert.True(c < 128, "NDJSON enviado deve ser ASCII (Unicode escapado)."));
    }

    [Fact]
    public async Task Logs_KeepStderrAndProtocolSummary_ButNeverTheTranscriptText()
    {
        var (script, media) = Fixture();
        var final = FixtureFiles.Read("whisper_transcribe_ok.json");
        var runner = new FakeProcessRunner(Worker((r, req) =>
        {
            r.StandardErrorLine?.Invoke("UserWarning: FP16 is not supported on CPU; using FP32 instead");
            r.StandardOutputLine?.Invoke($"{{\"protocol\":\"travelcut.whisper\",\"version\":1,\"requestId\":\"{req}\",\"event\":\"progress\",\"stage\":\"transcribing\",\"percent\":null}}");
            r.StandardOutputLine?.Invoke($"{{\"protocol\":\"travelcut.whisper\",\"version\":1,\"requestId\":\"{req}\",\"event\":\"heartbeat\",\"stage\":\"transcribing\",\"percent\":null}}");
            r.StandardOutputLine?.Invoke(final.Replace("__REQ__", req).Trim());
            return FakeProcessRunner.Completed(0, final.Replace("__REQ__", req));
        }));
        var observer = new CapturingObserver();

        await Service(runner, script).TranscribeAsync(media, Guid.NewGuid(), execution: observer);

        Assert.Contains(observer.Logs, l => l.Stream == "stderr" && l.Text.Contains("FP16"));
        Assert.Contains(observer.Logs, l => l.Stream == "stdout" && l.Text == "evento: etapa transcribing");
        Assert.Contains(observer.Logs, l => l.Text.Contains("conteúdo da transcrição omitido"));
        Assert.DoesNotContain(observer.Logs, l => l.Text.Contains("estrada"));
        Assert.DoesNotContain(observer.Logs, l => l.Text.Contains("heartbeat"));
    }

    [Theory]
    [InlineData(ProcessRunStatus.TimedOut, "timed_out")]
    [InlineData(ProcessRunStatus.Canceled, "canceled")]
    public async Task TimeoutAndCancellation_AreRecordedWithoutSuccess(ProcessRunStatus status, string expected)
    {
        var (script, media) = Fixture();
        var runner = new FakeProcessRunner(Worker((_, _) => FakeProcessRunner.Status(status)));
        var observer = new CapturingObserver();

        var outcome = await Service(runner, script).TranscribeAsync(media, Guid.NewGuid(), execution: observer);

        Assert.NotEqual("completed", outcome.Status);
        Assert.Equal(expected, observer.Result!.ProcessState);
        Assert.Null(observer.Result.ExitCode);
    }

    [Fact]
    public async Task ProcessExitWithoutValidResponse_IsFailure_WithExitCode()
    {
        var (script, media) = Fixture();
        var runner = new FakeProcessRunner(Worker((_, _) => FakeProcessRunner.Completed(1, "", "Traceback: ModuleNotFoundError: torch")));
        var observer = new CapturingObserver();

        var outcome = await Service(runner, script).TranscribeAsync(media, Guid.NewGuid(), execution: observer);

        Assert.Equal("failed", outcome.Status);
        Assert.Equal("failed", observer.Result!.ProcessState);
        Assert.Equal(1, observer.Result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(observer.Result.Error));
    }

    [Fact]
    public async Task NoProcessStarted_WhenPythonIsMissing_HasNoExecutionRecord()
    {
        var (script, media) = Fixture();
        var runner = new FakeProcessRunner(_ => FakeProcessRunner.NotStarted());
        var observer = new CapturingObserver();
        var service = new WhisperTranscriptionService(NullLogger<WhisperTranscriptionService>.Instance, runner,
            new WhisperServiceOptions(PythonPath: null, WorkerScriptPath: script));

        var outcome = await service.TranscribeAsync(media, Guid.NewGuid(), execution: observer);

        Assert.Equal("unavailable", outcome.Status);
        Assert.Null(observer.Info);
    }

    [Fact]
    public void Record_LimitsLogs_TruncatesLongLines_AndHidesSecrets()
    {
        var record = new WhisperExecutionRecord(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(record.ToDto());
        record.OnStarted(Info(@"C:\Python\python.exe", new[] { @"C:\w\whisper_worker.py" }));

        for (int i = 0; i < WhisperExecutionRecord.MaxLogLines + 25; i++) record.OnLog("stderr", $"linha {i}");
        record.OnLog("stderr", "HF_TOKEN token=abc123secreto api_key: zzz");
        record.OnLog("stderr", new string('x', 2000));

        var dto = record.ToDto()!;
        Assert.Equal(WhisperExecutionRecord.MaxLogLines, dto.LogLines.Count);
        Assert.Equal(27, dto.DroppedLogLines);
        Assert.DoesNotContain(dto.LogLines, l => l.Text.Contains("abc123secreto") || l.Text.Contains("zzz"));
        Assert.True(dto.LogLines[^1].Text.Length <= WhisperExecutionRecord.MaxLogLineLength + 1);
        Assert.Equal("running", dto.ProcessState);
    }

    [Fact]
    public void Literal_EscapesSingleAndTypographicQuotes_WithoutInterpolation()
    {
        Assert.Equal("'C:\\Viagem d''Ouro\\$env:PATH'", WhisperDiagnosticScript.Literal("C:\\Viagem d'Ouro\\$env:PATH"));
        Assert.Equal("'a\u2019\u2019b'", WhisperDiagnosticScript.Literal("a\u2019b"));
    }

    [Fact]
    public void CommandLine_FollowsWindowsArgvRules()
    {
        Assert.Equal(@"-3 ""C:\Pasta com espaço\worker.py""", WhisperDiagnosticScript.BuildCommandLine(new[] { "-3", @"C:\Pasta com espaço\worker.py" }));
        Assert.Equal(@"""a \""b\"" c""", WhisperDiagnosticScript.BuildCommandLine(new[] { "a \"b\" c" }));
        Assert.Equal(@"""C:\dir com espaço\\""", WhisperDiagnosticScript.BuildCommandLine(new[] { @"C:\dir com espaço\" }));
        Assert.Equal("\"\"", WhisperDiagnosticScript.BuildCommandLine(new[] { "" }));
    }

    [Fact]
    public void Script_UsesResolvedValues_AndContainsNoDownloadOrInstall()
    {
        var script = WhisperDiagnosticScript.Build(Info(PythonPath, new[] { @"K:\Viagem São Tomé\workers\whisper_worker.py" }));

        Assert.Contains("$psi.FileName = 'C:\\Ferramentas Python\\Python 3.11\\python.exe'", script);
        Assert.Contains("\"K:\\Viagem São Tomé\\workers\\whisper_worker.py\"", script);
        Assert.Contains("$psi.EnvironmentVariables['PYTHONUTF8'] = '1'", script);
        Assert.Contains("\"command\":\"transcribe\"", script);
        Assert.Contains("ExitCode", script);
        Assert.DoesNotContain("pip install", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-WebRequest", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-Expression", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Script_RunsInWindowsPowerShell_AgainstAFakeWorker_InPathWithSpacesAndUnicode()
    {
        var root = Path.Combine(Path.GetTempPath(), "TravelCut Diag ção " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // O "worker" é um script PowerShell que ecoa o stdin, escreve em stderr e sai com código 3.
            var fakeWorker = Path.Combine(root, "worker falso ç.ps1");
            File.WriteAllText(fakeWorker,
                "$entrada = [Console]::In.ReadToEnd()\n" +
                "foreach ($l in ($entrada -split \"`n\")) { if ($l.Trim()) { [Console]::Out.WriteLine('ECO:' + $l.Trim()) } }\n" +
                "[Console]::Error.WriteLine('aviso no stderr')\n" +
                "exit 3\n",
                new UTF8Encoding(true));

            var info = new WhisperExecutionInfo(
                RequestId: "req1",
                PythonExecutable: "powershell.exe",
                Arguments: new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", fakeWorker },
                WorkerScript: fakeWorker,
                WorkingDirectory: root,
                EnvironmentVariables: new Dictionary<string, string> { ["PYTHONUTF8"] = "1" },
                StandardInput: "{\"command\":\"transcribe\",\"requestId\":\"req1\",\"payload\":{\"mediaPath\":\"K:\\\\S\\u00e3o\\\\a.mp4\"}}\n{\"command\":\"exit\"}\n",
                MediaPath: @"K:\São\a.mp4",
                Model: "large-v3",
                Language: "pt",
                Device: "cpu");

            var scriptFile = Path.Combine(root, "diagnostico.ps1");
            File.WriteAllText(scriptFile, WhisperDiagnosticScript.Build(info), new UTF8Encoding(true));

            var result = await new ProcessRunner().RunAsync(new ProcessRunRequest(
                "powershell.exe",
                new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptFile },
                TimeSpan.FromSeconds(60),
                EnvironmentVariables: new Dictionary<string, string> { ["TEMP"] = root }));

            Assert.Equal(ProcessRunStatus.Completed, result.Status);
            Assert.Contains("ECO:{\"command\":\"transcribe\"", result.StandardOutput);
            Assert.Contains("ECO:{\"command\":\"exit\"}", result.StandardOutput);
            Assert.Contains("aviso no stderr", result.StandardOutput);
            Assert.Contains("CODIGO DE SAIDA: 3", result.StandardOutput);

            var saved = Directory.GetFiles(root, "travelcut-whisper-diagnostico-*.log").Single();
            var log = File.ReadAllText(saved);
            Assert.Contains("ECO:", log);
            Assert.Contains("CODIGO DE SAIDA: 3", log);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    private static WhisperExecutionInfo Info(string python, IReadOnlyList<string> args) => new(
        RequestId: "req1",
        PythonExecutable: python,
        Arguments: args,
        WorkerScript: args[^1],
        WorkingDirectory: @"C:\App",
        EnvironmentVariables: new Dictionary<string, string> { ["PYTHONUTF8"] = "1", ["PYTHONIOENCODING"] = "utf-8" },
        StandardInput: "{\"command\":\"transcribe\",\"requestId\":\"req1\",\"payload\":{}}\n{\"command\":\"exit\"}\n",
        MediaPath: @"K:\a.mp4",
        Model: "large-v3",
        Language: "pt",
        Device: "cpu");
}
