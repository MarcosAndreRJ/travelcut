using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Infrastructure.Audio;
using TravelCut.Infrastructure.Cache;
using TravelCut.Infrastructure.Locks;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Repositories;

namespace TravelCut.UnitTests.Audio;

/// <summary>Fake de Whisper: nunca sintetiza texto. Available=false simula Python/Whisper ausentes.</summary>
public sealed class FakeWhisperService : IWhisperTranscriptionService
{
    private int _transcribeCalls;

    public bool Available { get; set; } = true;
    public TranscriptionOutcome? Outcome { get; set; }
    public bool WaitForCancellation { get; set; }
    /// <summary>Telemetria entregue antes de bloquear ou responder.</summary>
    public List<TranscriptionProgressUpdate> ProgressScript { get; } = new();
    public TaskCompletionSource<bool>? ProgressDelivered { get; set; }

    public int TranscribeCalls => Volatile.Read(ref _transcribeCalls);
    public int InvalidateCalls { get; private set; }

    public void InvalidateEnvironment() => InvalidateCalls++;

    public Task<WhisperProbeResult> ProbeEnvironmentAsync(CancellationToken ct = default) =>
        Task.FromResult(new WhisperProbeResult(
            HasPython: Available,
            HasCuda: false,
            Device: "cpu",
            HasWhisper: Available,
            Engine: Available ? "fake-whisper 1.0" : null,
            DefaultModel: "large-v3",
            StatusSummary: Available ? "fake disponível" : "fake indisponível"));

    public Task<string?> FindPythonExecutableAsync(CancellationToken ct = default) =>
        Task.FromResult(Available ? "fake-python" : (string?)null);

    public async Task<TranscriptionOutcome> TranscribeAsync(
        string mediaPath,
        Guid mediaId,
        string model = "large-v3",
        string language = "pt",
        CancellationToken ct = default,
        IProgress<TranscriptionProgressUpdate>? progress = null,
        IWhisperExecutionObserver? execution = null)
    {
        Interlocked.Increment(ref _transcribeCalls);
        if (!Available)
        {
            return new TranscriptionOutcome("unavailable", null, "Python/Whisper indisponível (teste).", null, null);
        }
        execution?.OnStarted(new WhisperExecutionInfo("req-fake", "fake-python", new[] { "fake_worker.py" }, "fake_worker.py",
            "C:\\App", new Dictionary<string, string> { ["PYTHONUTF8"] = "1" }, "{\"command\":\"transcribe\"}\n{\"command\":\"exit\"}\n",
            mediaPath, model, language, "cpu"));
        execution?.OnLog("stderr", "aviso de teste");
        foreach (var update in ProgressScript) progress?.Report(update);
        ProgressDelivered?.TrySetResult(true);
        if (WaitForCancellation)
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        var result = Outcome ?? new TranscriptionOutcome("failed", null, "Sem resultado configurado no fake.", null, null);
        execution?.OnFinished(new WhisperExecutionResult(result.Status == "completed" ? "completed" : "failed", 0, result.Reason));
        return result;
    }
}

/// <summary>Fake de FFmpeg/silencedetect: Available=false simula FFmpeg ausente.</summary>
public sealed class FakeSilenceService : ISilenceDetectionService
{
    private int _detectCalls;

    public bool FfmpegAvailable { get; set; } = true;
    public SilenceDetectionOutcome? Outcome { get; set; }
    public List<HesitationCandidateDto> Hesitations { get; set; } = new();

    public int DetectCalls => Volatile.Read(ref _detectCalls);

    public bool IsFfmpegAvailable() => FfmpegAvailable;

    public string? GetFfmpegVersion() => FfmpegAvailable ? "fake 7.0" : null;

    public Task<SilenceDetectionOutcome> DetectSilenceAsync(
        string mediaPath,
        SilenceDetectionOptions? options = null,
        CancellationToken ct = default)
    {
        Interlocked.Increment(ref _detectCalls);
        if (!FfmpegAvailable)
        {
            return Task.FromResult(new SilenceDetectionOutcome("unavailable", new List<VadSilenceSegmentDto>(), "FFmpeg indisponível (teste)."));
        }
        return Task.FromResult(Outcome ?? new SilenceDetectionOutcome("failed", new List<VadSilenceSegmentDto>(), "Sem resultado configurado no fake."));
    }

    public List<HesitationCandidateDto> DetectHesitationsAndFillers(TranscriptDto transcript, IReadOnlyList<VadSilenceSegmentDto> silences) =>
        Hesitations.ToList();
}

/// <summary>
/// Pipeline real (PipelineJobManager + repositórios EF + SQLite em memória + cache em pasta temporária)
/// com serviços de Whisper/FFmpeg substituídos por fakes.
/// </summary>
public sealed class PipelineHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <param name="transcriptRepositoryFactory">Permite substituir o repositório de transcrição (ex.: forçar falha de persistência).</param>
    public PipelineHarness(
        FakeWhisperService? whisper = null,
        FakeSilenceService? silence = null,
        Func<TravelCutDbContext, ITranscriptRepository>? transcriptRepositoryFactory = null)
    {
        Whisper = whisper ?? new FakeWhisperService { Available = false };
        Silence = silence ?? new FakeSilenceService { FfmpegAvailable = false };
        Root = Path.Combine(Path.GetTempPath(), "TravelCutPipeline_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);

        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TravelCutDbContext>().UseSqlite(_connection).Options;
        Db = new TravelCutDbContext(options);
        Db.Database.EnsureCreated();

        var services = new ServiceCollection();
        // DbContext por escopo, como em produção (mesma conexão SQLite em memória, compartilhada pelos contextos).
        services.AddScoped(_ => new TravelCutDbContext(options));
        services.AddScoped<IProjectRepository, SqliteProjectRepository>();
        services.AddScoped<IMediaRepository, SqliteMediaRepository>();
        services.AddScoped<ITranscriptRepository>(sp =>
            (transcriptRepositoryFactory ?? (db => new SqliteTranscriptRepository(db)))(sp.GetRequiredService<TravelCutDbContext>()));
        services.AddScoped<IEditPlanRepository, SqliteEditPlanRepository>();
        services.AddScoped<IMediaCacheManager, MediaCacheManager>();
        Services = services.BuildServiceProvider();

        Locks = new FileProjectOperationLock();
        Manager = new PipelineJobManager(
            NullLogger<PipelineJobManager>.Instance,
            Services.GetRequiredService<IServiceScopeFactory>(),
            Whisper,
            Silence,
            new SceneClassifierService(NullLogger<SceneClassifierService>.Instance),
            new CutCandidateService(NullLogger<CutCandidateService>.Instance),
            Locks);
    }

    public TravelCutDbContext Db { get; }
    public ServiceProvider Services { get; }
    public PipelineJobManager Manager { get; }
    /// <summary>Trava real entre processos (arquivos na pasta temporária do projeto).</summary>
    public IProjectOperationLock Locks { get; }
    public FakeWhisperService Whisper { get; }
    public FakeSilenceService Silence { get; }
    public string Root { get; }

    public Project CreateProject(string name = "Projeto Pipeline")
    {
        var project = new Project
        {
            ProjectId = Guid.NewGuid(),
            Name = name,
            RootPath = Root,
            CreatedAtUtc = DateTime.UtcNow,
            LastModifiedAtUtc = DateTime.UtcNow
        };
        Db.Projects.Add(project);
        Db.SaveChanges();
        return project;
    }

    /// <summary>Cria o arquivo físico (conteúdo fictício) e o registro de mídia.</summary>
    public MediaAsset AddMedia(Project project, string relativePath, MediaKind kind = MediaKind.Video, string fingerprint = "fp-test", MediaMetadata? metadata = null)
    {
        string full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[32]);

        var asset = new MediaAsset
        {
            MediaId = Guid.NewGuid(),
            ProjectId = project.ProjectId,
            RelativePath = relativePath.Replace('\\', '/'),
            ByteLength = 32,
            LastWriteUtc = DateTime.UtcNow,
            Fingerprint = fingerprint,
            Kind = kind
        };
        if (metadata != null)
        {
            metadata.MediaId = asset.MediaId;
            asset.Metadata = metadata;
        }
        Db.MediaAssets.Add(asset);
        Db.SaveChanges();
        return asset;
    }

    public async Task<PipelineStatusDto> WaitForFinishAsync(Guid projectId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            var status = await Manager.GetStatusAsync(projectId);
            if (status.State is "completed" or "completed_with_issues" or "failed" or "canceled")
            {
                return status;
            }
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Análise não terminou; estado atual: {status.State}");
            }
            await Task.Delay(20);
        }
    }

    public void Dispose()
    {
        Services.Dispose();
        Db.Dispose();
        _connection.Dispose();
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Limpeza best-effort de pasta temporária.
        }
    }
}
