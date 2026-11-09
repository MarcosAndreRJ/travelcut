using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Infrastructure.Import;
using Xunit;

namespace TravelCut.Tests.Import;

public class MediaImportServiceTests
{
    private const int ThreeMiB = 3 * 1024 * 1024 + 123;

    [Fact]
    public async Task CopiesFileWithVerifiedHashAndPreservesTimestamp()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);

        var bytes = TestFiles.RandomBytes(ThreeMiB);
        var mtime = new DateTime(2024, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "cartao", "DJI_0001.MP4"), bytes, mtime);

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }));
        Assert.True(start.Success);

        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("completed", status.State);
        Assert.Equal("done", status.Stage);
        var item = Assert.Single(status.Items);
        Assert.Equal("imported", item.Result);
        Assert.Equal("1-Bruto/DJI_0001.MP4", item.DestinationRelativePath);

        string dest = Path.Combine(root, "1-Bruto", "DJI_0001.MP4");
        Assert.True(File.Exists(dest));
        Assert.Equal(TestFiles.Sha256(bytes), TestFiles.Sha256File(dest));
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(dest));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "1-Bruto"), "*.tcpart"));
        Assert.Equal(status.BytesTotal, status.BytesCopied);

        var asset = Assert.Single(h.Media.Assets);
        Assert.Equal("1-Bruto/DJI_0001.MP4", asset.RelativePath);
        Assert.Equal(MediaMetadataStatus.NotRequested, asset.Metadata!.MetadataStatus);
        Assert.Equal(0, h.Ffprobe.ExtractCalls);
    }

    [Fact]
    public async Task SourceFilesAreNeverModifiedOrDeleted()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);

        var bytes = TestFiles.RandomBytes(2 * 1024 * 1024);
        var mtime = new DateTime(2023, 7, 4, 8, 30, 0, DateTimeKind.Utc);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "clip.mov"), bytes, mtime);
        string hashBefore = TestFiles.Sha256File(source);

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }));
        await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.True(File.Exists(source));
        Assert.Equal(hashBefore, TestFiles.Sha256File(source));
        Assert.Equal(bytes.Length, new FileInfo(source).Length);
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(source));
    }

    [Fact]
    public async Task NameConflictWithDifferentContentIsRenamedAndExistingFileIsKept()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);

        string existing = Path.Combine(root, "1-Bruto", "clip.mp4");
        var oldBytes = System.Text.Encoding.UTF8.GetBytes("conteúdo antigo, diferente");
        File.WriteAllBytes(existing, oldBytes);

        var newBytes = TestFiles.RandomBytes(64 * 1024, seed: 7);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "clip.mp4"), newBytes);

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        var item = Assert.Single(status.Items);
        Assert.Equal("renamed", item.Result);
        Assert.Equal("1-Bruto/clip (1).mp4", item.DestinationRelativePath);

        // O arquivo existente nunca é sobrescrito.
        Assert.Equal(oldBytes, File.ReadAllBytes(existing));
        Assert.Equal(TestFiles.Sha256(newBytes), TestFiles.Sha256File(Path.Combine(root, "1-Bruto", "clip (1).mp4")));
        Assert.Equal("completed", status.State);
    }

    [Fact]
    public async Task IdenticalExistingFileIsSkippedAsDuplicateAndStillRegistered()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);

        var bytes = TestFiles.RandomBytes(100_000, seed: 11);
        string existing = TestFiles.WriteSource(Path.Combine(root, "1-Bruto", "clip.mp4"), bytes);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "clip.mp4"), bytes);

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        var item = Assert.Single(status.Items);
        Assert.Equal("skipped_duplicate", item.Result);
        Assert.Equal("1-Bruto/clip.mp4", item.DestinationRelativePath);
        Assert.False(File.Exists(Path.Combine(root, "1-Bruto", "clip (1).mp4")));
        Assert.Equal(bytes.Length, new FileInfo(existing).Length);

        // Duplicata ainda é registrada na biblioteca.
        Assert.Contains(h.Media.Assets, a => a.RelativePath == "1-Bruto/clip.mp4");
        Assert.Equal("completed", status.State);
    }

    [Fact]
    public async Task SubfolderWithParentSegmentIsRejectedBeforeAnyWrite()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "a.mp4"), TestFiles.RandomBytes(1024));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, subfolder: "..\\x"));

        Assert.False(start.Success);
        Assert.Equal("INVALID_SUBFOLDER", start.ErrorCode);
        Assert.False(Directory.Exists(Path.Combine(root, "x")));
        Assert.False(Directory.Exists(Path.Combine(tmp.Path, "x")));
        Assert.Empty(h.Media.Assets);
    }

    [Fact]
    public async Task CopyDisabledWithSourceOutsideDestinationFailsItemWithClearMessage()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "a.mp4"), TestFiles.RandomBytes(4096));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, copyFiles: false));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        var item = Assert.Single(status.Items);
        Assert.Equal("failed", item.Result);
        Assert.Contains("Cópia desativada", item.Message);
        Assert.Equal("completed_with_errors", status.State);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "1-Bruto")));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task CancellationRemovesOwnPartialFileAndKeepsAlreadyImportedItems()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");

        var reached = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        using var h = new ImportHarness(new MediaImportOptions
        {
            // Pausa somente no primeiro bloco do arquivo grande (>= 1 MiB), dando tempo de cancelar com determinismo.
            CopyChunkHook = total =>
            {
                if (total >= 1024 * 1024 && !reached.IsSet)
                {
                    reached.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }
            }
        });
        var project = h.CreateProject(root);

        string small = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "a-small.mp4"), TestFiles.RandomBytes(10 * 1024, seed: 3));
        string big = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "b-big.mp4"), TestFiles.RandomBytes(ThreeMiB, seed: 5));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { small, big }));
        Assert.True(start.Success);
        Guid jobId = start.JobId!.Value;

        Assert.True(await Task.Run(() => reached.Wait(TimeSpan.FromSeconds(10))), "O hook de cópia não foi atingido.");
        var cancel = h.Service.Cancel(jobId);
        Assert.True(cancel.Canceled);
        release.Set();

        var status = await ImportHarness.WaitForFinalAsync(h.Service, jobId);

        Assert.Equal("canceled", status.State);
        var small_ = status.Items.Single(i => i.SourcePath == small);
        var big_ = status.Items.Single(i => i.SourcePath == big);
        Assert.Equal("imported", small_.Result);
        Assert.Equal("canceled", big_.Result);

        string destDir = Path.Combine(root, "1-Bruto");
        Assert.True(File.Exists(Path.Combine(destDir, "a-small.mp4")));
        Assert.False(File.Exists(Path.Combine(destDir, "b-big.mp4")));
        Assert.Empty(Directory.GetFiles(destDir, "*.tcpart"));

        // Já importado fica registrado.
        Assert.Contains(h.Media.Assets, a => a.RelativePath == "1-Bruto/a-small.mp4");
        Assert.True(File.Exists(big));
        release.Dispose();
        reached.Dispose();
    }

    [Fact]
    public async Task InsufficientFreeSpaceFailsPreflightBeforeCopying()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        h.FreeSpace.Available = 10 * 1024; // menos que a margem de 1 GiB

        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "a.mp4"), TestFiles.RandomBytes(50 * 1024));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("failed", status.State);
        Assert.Equal("preflight", status.Stage);
        Assert.Contains("Espaço insuficiente", status.Message);
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "1-Bruto")));
        Assert.Empty(h.Media.Assets);
        Assert.Equal(0, status.BytesCopied);
    }

    [Fact]
    public async Task OnlyOneImportIsAllowedPerProjectWhileRunning()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");

        var reached = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        using var h = new ImportHarness(new MediaImportOptions
        {
            CopyChunkHook = total =>
            {
                if (!reached.IsSet)
                {
                    reached.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }
            }
        });
        var project = h.CreateProject(root);
        string first = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "first.mp4"), TestFiles.RandomBytes(8 * 1024));
        string second = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "second.mp4"), TestFiles.RandomBytes(8 * 1024));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { first }));
        Assert.True(start.Success);
        Assert.True(await Task.Run(() => reached.Wait(TimeSpan.FromSeconds(10))));

        var concurrent = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { second }));
        Assert.False(concurrent.Success);
        Assert.Equal("IMPORT_IN_PROGRESS", concurrent.ErrorCode);

        release.Set();
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);
        Assert.Equal("completed", status.State);

        // Após terminar, um novo import é aceito.
        var after = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { second }));
        Assert.True(after.Success);
        await ImportHarness.WaitForFinalAsync(h.Service, after.JobId!.Value);
        release.Dispose();
        reached.Dispose();
    }

    [Fact]
    public async Task TagsAreNormalizedAndAppliedToImportedAssets()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "praia.mp4"), TestFiles.RandomBytes(2048));

        var start = await h.Service.StartAsync(ImportHarness.Request(
            project.ProjectId,
            new[] { source },
            tags: new[] { "  viagem ", "Viagem", "", "praia" }));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("completed", status.State);
        var asset = Assert.Single(h.Media.Assets);
        Assert.Equal(new[] { "viagem", "praia" }, asset.Tags);
    }

    [Fact]
    public async Task FileAlreadyInsideDestinationIsRegisteredInPlaceWithoutCopy()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);

        var bytes = TestFiles.RandomBytes(4096, seed: 9);
        string inPlace = TestFiles.WriteSource(Path.Combine(root, "1-Bruto", "ja-aqui.mp4"), bytes);

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { inPlace }));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        var item = Assert.Single(status.Items);
        Assert.Equal("registered_in_place", item.Result);
        Assert.Equal("1-Bruto/ja-aqui.mp4", item.DestinationRelativePath);
        Assert.Equal(0, status.BytesTotal);
        Assert.Equal(1, Directory.GetFiles(Path.Combine(root, "1-Bruto")).Length);
        Assert.Contains(h.Media.Assets, a => a.RelativePath == "1-Bruto/ja-aqui.mp4");
    }

    [Fact]
    public async Task StaleTcpartFromAnotherRunIsNeverDeleted()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "x.mp4"), TestFiles.RandomBytes(2048));
        string stale = Path.Combine(root, "1-Bruto", "x.mp4.tcpart");
        File.WriteAllText(stale, "resto de execução anterior");

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        var item = Assert.Single(status.Items);
        Assert.Equal("failed", item.Result);
        Assert.Contains(".tcpart", item.Message);
        Assert.True(File.Exists(stale));
        Assert.False(File.Exists(Path.Combine(root, "1-Bruto", "x.mp4")));
    }

    [Fact]
    public async Task ThumbnailFailureDoesNotFailImportedItem()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "foto.jpg"), TestFiles.RandomBytes(2048));

        var start = await h.Service.StartAsync(ImportHarness.Request(
            project.ProjectId, new[] { source }, generateThumbnails: true));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("completed", status.State);
        Assert.Equal("imported", Assert.Single(status.Items).Result);
        Assert.Contains("FFmpeg", status.Message);
        Assert.Single(h.Thumbnails.Requested);
        Assert.Equal(Path.Combine(root, "1-Bruto", "foto.jpg"), h.Thumbnails.Requested[0]);
    }

    [Fact]
    public async Task DetectSpeechIsReportedAsUnavailableAndNothingIsQueued()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "fala.mp4"), TestFiles.RandomBytes(2048));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, detectSpeech: true));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("unavailable", status.SpeechAnalysis);
        Assert.Contains("nada foi enfileirado", status.Message);
        Assert.Equal("completed", status.State);
    }

    [Fact]
    public async Task SpeechAnalysisIsNotRequestedWhenDisabled()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "silencio.mp4"), TestFiles.RandomBytes(2048));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, detectSpeech: false));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("not_requested", status.SpeechAnalysis);
    }

    [Fact]
    public async Task SpeechAnalysis_WhenSupported_StartsPipelineAndRemainsQueued()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        var fakePipeline = new FakePipelineJobManager
        {
            WhisperAvailable = true,
            FfmpegAvailable = true
        };
        using var h = new ImportHarness(pipelineJobs: fakePipeline);
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "entrevista.mp4"), TestFiles.RandomBytes(2048));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, detectSpeech: true));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("completed", status.State);
        Assert.Equal("queued", status.SpeechAnalysis);
        Assert.Single(fakePipeline.StartedRequests);
        var pipelineReq = fakePipeline.StartedRequests[0];
        Assert.Equal(project.ProjectId, pipelineReq.ProjectId);
        Assert.NotNull(pipelineReq.MediaIds);
        Assert.Single(pipelineReq.MediaIds);
        Assert.Contains("enfileirada", status.Message);
    }

    [Fact]
    public async Task SpeechAnalysis_WhenWhisperUnavailable_ReportsReasonAndQueuesNothing()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        var fakePipeline = new FakePipelineJobManager
        {
            WhisperAvailable = false,
            Reasons = new List<string> { "Python 3.10+ não encontrado no PATH" }
        };
        using var h = new ImportHarness(pipelineJobs: fakePipeline);
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "video.mp4"), TestFiles.RandomBytes(2048));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, detectSpeech: true));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("completed", status.State);
        Assert.Equal("unavailable", status.SpeechAnalysis);
        Assert.Empty(fakePipeline.StartedRequests);
        Assert.Contains("Python 3.10+ não encontrado", status.Message);
        Assert.Contains("nada foi enfileirado", status.Message);
    }

    [Fact]
    public async Task SpeechAnalysis_WhenPipelineAlreadyRunning_PreservesQueuedWithNote()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        var fakePipeline = new FakePipelineJobManager
        {
            WhisperAvailable = true,
            ShouldThrowInProgress = true
        };
        using var h = new ImportHarness(pipelineJobs: fakePipeline);
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "clipe.mp4"), TestFiles.RandomBytes(2048));
        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, detectSpeech: true));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("completed", status.State);
        Assert.Equal("queued", status.SpeechAnalysis);
        Assert.Contains("já está em andamento", status.Message);
    }

    [Fact]
    public async Task DetectSpeechWithoutRegisteredPipelineMarksSpeechUnavailable()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "fala.mp4"), TestFiles.RandomBytes(2048));
        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, detectSpeech: true));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("completed", status.State);
        Assert.Equal("unavailable", status.SpeechAnalysis);
        Assert.Contains("não está registrado", status.Message);
    }

    [Fact]
    public async Task DetectSpeechWithRegisteredPipelineTriggersSpeechAnalysisJob()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness(registerPipeline: true);
        var project = h.CreateProject(root);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "fala.mp4"), TestFiles.RandomBytes(2048));
        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, detectSpeech: true));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("completed", status.State);
        Assert.Equal("queued", status.SpeechAnalysis);
        Assert.NotNull(h.PipelineJobs);
        Assert.Single(h.PipelineJobs.StartedRequests);
        var req = h.PipelineJobs.StartedRequests[0];
        Assert.Equal(project.ProjectId, req.ProjectId);
        Assert.Single(req.MediaIds!);
    }

    [Fact]
    public async Task DetectSpeech_SlowEnvironmentProbe_DoesNotBlockStart_AndIsDecidedInsideJob()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness(registerPipeline: true);
        var project = h.CreateProject(root);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.PipelineJobs!.ProbeGate = gate; // probe de ambiente/status "trava" até liberarmos
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "fala.mp4"), TestFiles.RandomBytes(2048));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, detectSpeech: true));
        sw.Stop();

        Assert.True(start.Success);
        Assert.True(sw.ElapsedMilliseconds < 200, $"StartAsync levou {sw.ElapsedMilliseconds} ms com probe lento");

        // Job ainda não terminou: aguarda a decisão da fala (probe em segundo plano).
        await Task.Delay(300);
        Assert.False(h.Service.GetStatus(start.JobId!.Value)!.State is "completed" or "completed_with_errors" or "failed");
        Assert.Empty(h.PipelineJobs.StartedRequests);

        gate.SetResult(true);
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("completed", status.State);
        Assert.Equal("queued", status.SpeechAnalysis);
        Assert.Single(h.PipelineJobs.StartedRequests);
    }

    [Fact]
    public async Task DetectSpeech_CachedEnvironmentWithoutWhisper_IsUnavailableImmediately_WithoutProbe()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness(registerPipeline: true);
        var project = h.CreateProject(root);
        h.PipelineJobs!.CachedEnvironment = new AnalysisEnvironmentDto(
            true, "7.1", true, false, null, "large-v3", true, new List<string>(),
            Device: "cuda", ModelCached: false, CpuFallback: false,
            WhisperUnavailableReason: "Modelo Whisper large-v3 não está no cache local.");
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "fala.mp4"), TestFiles.RandomBytes(2048));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, detectSpeech: true));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("unavailable", status.SpeechAnalysis);
        Assert.Contains("cache local", status.Message);
        Assert.Empty(h.PipelineJobs.StartedRequests);
        Assert.Equal(0, h.PipelineJobs.EnvironmentCalls);
    }

    [Fact]
    public async Task AnalyzeMetadataUsesFfprobeAndStoresStatus()
    {
        using var tmp = new TempDirectory();
        string root = Path.Combine(tmp.Path, "Projeto");
        using var h = new ImportHarness();
        var project = h.CreateProject(root);
        h.Ffprobe.Available = true;
        h.Ffprobe.Responder = path => new ExtractedMediaInfo(
            Codec: "h264", Width: 1920, Height: 1080, TimeBase: new(1, 30), FrameRate: 30, DurationSeconds: 12.5,
            DurationTicks: 375, AudioChannels: 2, AudioSampleRate: 48000, CreationTimeUtc: null,
            SourceDateEvidence: "creation_time_utc_tag", IsVariableFrameRate: false, RotationDegrees: 0,
            CameraModel: null, GpsLocation: null, MetadataStatus: MediaMetadataStatus.Ok);
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "analise.mp4"), TestFiles.RandomBytes(2048));

        var start = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, analyzeMetadata: true));
        var status = await ImportHarness.WaitForFinalAsync(h.Service, start.JobId!.Value);

        Assert.Equal("completed", status.State);
        var asset = Assert.Single(h.Media.Assets);
        Assert.Equal(MediaMetadataStatus.Ok, asset.Metadata!.MetadataStatus);
        Assert.Equal(12.5, asset.Metadata.DurationSeconds);
        Assert.Equal(1, h.Ffprobe.ExtractCalls);
    }

    [Fact]
    public async Task StartRejectsInvalidDestinationAndMissingProject()
    {
        using var tmp = new TempDirectory();
        using var h = new ImportHarness();
        var project = h.CreateProject(Path.Combine(tmp.Path, "Projeto"));
        string source = TestFiles.WriteSource(Path.Combine(tmp.Path, "origem", "a.mp4"), TestFiles.RandomBytes(16));

        var badDestination = await h.Service.StartAsync(ImportHarness.Request(project.ProjectId, new[] { source }, destination: "3-Trabalho"));
        Assert.False(badDestination.Success);
        Assert.Equal("INVALID_DESTINATION", badDestination.ErrorCode);

        var missingProject = await h.Service.StartAsync(ImportHarness.Request(Guid.NewGuid(), new[] { source }));
        Assert.False(missingProject.Success);
        Assert.Equal("PROJECT_NOT_FOUND", missingProject.ErrorCode);
    }

    [Fact]
    public async Task CancelAndStatusForUnknownJobReturnNegativeResults()
    {
        using var h = new ImportHarness();
        Assert.Null(h.Service.GetStatus(Guid.NewGuid()));
        Assert.False(h.Service.Cancel(Guid.NewGuid()).Canceled);
    }
}
