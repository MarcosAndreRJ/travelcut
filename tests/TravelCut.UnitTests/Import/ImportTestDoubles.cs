using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Infrastructure.Import;

namespace TravelCut.Tests.Import;

internal sealed class InMemoryProjectRepository : IProjectRepository
{
    public List<Project> Projects { get; } = new();

    public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult(Projects.FirstOrDefault(p => p.ProjectId == projectId));

    public Task<Project?> GetByRootPathAsync(string rootPath, CancellationToken ct = default)
        => Task.FromResult(Projects.FirstOrDefault(p => p.RootPath == rootPath));

    public Task<List<Project>> ListRecentAsync(int limit = 10, CancellationToken ct = default)
        => Task.FromResult(Projects.Take(limit).ToList());

    public Task SaveAsync(Project project, CancellationToken ct = default)
    {
        Projects.RemoveAll(p => p.ProjectId == project.ProjectId);
        Projects.Add(project);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryMediaRepository : IMediaRepository
{
    public List<MediaAsset> Assets { get; } = new();

    public Task<MediaAsset?> GetByIdAsync(Guid mediaId, CancellationToken ct = default)
        => Task.FromResult(Assets.FirstOrDefault(a => a.MediaId == mediaId));

    public Task<List<MediaAsset>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
        => Task.FromResult(Assets.Where(a => a.ProjectId == projectId).ToList());

    public Task SaveAsync(MediaAsset asset, CancellationToken ct = default)
    {
        Assets.RemoveAll(a => a.MediaId == asset.MediaId);
        Assets.Add(asset);
        return Task.CompletedTask;
    }

    public async Task SaveBatchAsync(IEnumerable<MediaAsset> assets, CancellationToken ct = default)
    {
        foreach (var asset in assets.ToList())
        {
            await SaveAsync(asset, ct);
        }
    }
}

/// <summary>ffprobe falso: nada é executado na VM. Responde conforme o teste configurar.</summary>
internal sealed class FakeFfprobeExtractor : IFfprobeExtractor
{
    public bool Available { get; set; }
    public Func<string, ExtractedMediaInfo>? Responder { get; set; }
    public int ExtractCalls;

    public bool IsFfprobeAvailable() => Available;

    public Task<ExtractedMediaInfo> ExtractMetadataAsync(string filePath, CancellationToken ct = default)
    {
        Interlocked.Increment(ref ExtractCalls);
        var info = Responder?.Invoke(filePath) ?? ExtractedMediaInfo.Unavailable(filePath, MediaMetadataStatus.Failed);
        return Task.FromResult(info);
    }
}

internal sealed class FakeFreeSpaceProvider : IFreeSpaceProvider
{
    public long Available { get; set; } = long.MaxValue / 2;
    public List<string> QueriedPaths { get; } = new();

    public long GetAvailableFreeSpace(string path)
    {
        QueriedPaths.Add(path);
        return Available;
    }
}

internal sealed class FakeThumbnailService : IThumbnailService
{
    private readonly object _sync = new();
    private readonly List<string> _requested = new();

    public ThumbnailResult Result { get; set; } = new(ThumbnailStatus.FfmpegUnavailable, null, "FFmpeg fake indisponível");

    public IReadOnlyList<string> Requested
    {
        get { lock (_sync) return _requested.ToList(); }
    }

    public Task<ThumbnailResult> GenerateAsync(string projectRoot, string mediaPath, double atSeconds, CancellationToken ct = default)
    {
        lock (_sync) _requested.Add(mediaPath);
        return Task.FromResult(Result);
    }
}

internal sealed class FakePipelineJobManager : IPipelineJobManager
{
    private readonly object _sync = new();

    public bool WhisperAvailable { get; set; } = true;
    public bool FfmpegAvailable { get; set; } = true;
    public List<string> Reasons { get; set; } = new();
    public List<StartPipelineRequest> StartedRequests { get; } = new();
    public Guid NextJobId { get; set; } = Guid.NewGuid();
    public Guid MockJobId { get => NextJobId; set => NextJobId = value; }
    public bool ShouldThrowInProgress { get; set; } = false;
    public bool ShouldStart { get => !ShouldThrowInProgress; set => ShouldThrowInProgress = !value; }
    public string? FailReason { get; set; }
    public PipelineStatusDto? CurrentStatus { get; set; }

    public Task<Guid> StartAsync(StartPipelineRequest request, CancellationToken ct = default)
    {
        lock (_sync)
        {
            if (ShouldThrowInProgress)
            {
                throw new InvalidOperationException(FailReason ?? "PIPELINE_IN_PROGRESS");
            }
            StartedRequests.Add(request);
            return Task.FromResult(NextJobId);
        }
    }

    public Task<PipelineStatusDto> GetStatusAsync(Guid projectId, CancellationToken ct = default)
    {
        return Task.FromResult(CurrentStatus ?? new PipelineStatusDto(
            JobId: NextJobId,
            ProjectId: projectId,
            State: "completed",
            Stages: new List<PipelineStageStatusDto>(),
            Media: new List<PipelineMediaStatusDto>(),
            CurrentMedia: null,
            Environment: null,
            StartedAtUtc: DateTime.UtcNow,
            FinishedAtUtc: DateTime.UtcNow));
    }

    public bool Cancel(Guid jobId) => true;

    public Task<AnalysisEnvironmentDto> GetEnvironmentAsync(CancellationToken ct = default, bool forceRefresh = false)
    {
        return Task.FromResult(new AnalysisEnvironmentDto(
            FfmpegAvailable: FfmpegAvailable,
            FfmpegVersion: "7.1",
            PythonAvailable: true,
            WhisperAvailable: WhisperAvailable,
            WhisperEngine: "faster-whisper",
            WhisperModel: "large-v3",
            CudaAvailable: true,
            Reasons: Reasons));
    }

    public Task<MediaAnalysisDto?> GetMediaAnalysisAsync(Guid projectId, Guid mediaId, CancellationToken ct = default)
    {
        return Task.FromResult<MediaAnalysisDto?>(null);
    }
}

/// <summary>Provedor de serviços mínimo: devolve instâncias pré-criadas por tipo.</summary>
internal sealed class FixedServiceProvider : IServiceProvider
{
    private readonly IReadOnlyDictionary<Type, object> _services;

    public FixedServiceProvider(IReadOnlyDictionary<Type, object> services) => _services = services;

    public object? GetService(Type serviceType) => _services.TryGetValue(serviceType, out var s) ? s : null;
}

internal sealed class FixedScopeFactory : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
{
    private readonly IServiceProvider _provider;

    public FixedScopeFactory(IServiceProvider provider) => _provider = provider;

    public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() => new Scope(_provider);

    private sealed class Scope : Microsoft.Extensions.DependencyInjection.IServiceScope
    {
        public Scope(IServiceProvider provider) => ServiceProvider = provider;
        public IServiceProvider ServiceProvider { get; }
        public void Dispose() { }
    }
}

/// <summary>Diretório temporário único, removido ao final do teste.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TravelCutImport_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Limpeza best-effort de temporários de teste.
        }
        catch (UnauthorizedAccessException)
        {
            // Limpeza best-effort de temporários de teste.
        }
    }
}

/// <summary>Montagem do serviço de importação com repositórios em memória e ffprobe/ffmpeg falsos.</summary>
internal sealed class ImportHarness : IDisposable
{
    public ImportHarness(MediaImportOptions? options = null, FakePipelineJobManager? pipelineJobs = null, bool registerPipeline = false)
    {
        if (registerPipeline && pipelineJobs == null)
        {
            pipelineJobs = new FakePipelineJobManager();
        }
        PipelineJobs = pipelineJobs;
        var scan = new ScanMediaLibraryUseCase(
            Projects,
            Media,
            new MediaScannerService(),
            Ffprobe,
            NullLogger<ScanMediaLibraryUseCase>.Instance);

        var services = new Dictionary<Type, object>
        {
            [typeof(ScanMediaLibraryUseCase)] = scan,
            [typeof(IProjectRepository)] = Projects,
            [typeof(IMediaRepository)] = Media,
            [typeof(IThumbnailService)] = Thumbnails
        };

        if (registerPipeline && pipelineJobs == null)
        {
            pipelineJobs = new FakePipelineJobManager();
        }

        PipelineJobs = pipelineJobs;
        if (PipelineJobs != null)
        {
            services[typeof(IPipelineJobManager)] = PipelineJobs;
        }

        var provider = new FixedServiceProvider(services);
        var scopeFactory = new FixedScopeFactory(provider);
        Service = new MediaImportService(
            scopeFactory,
            FreeSpace,
            NullLogger<MediaImportService>.Instance,
            options,
            PipelineJobs);
    }

    public InMemoryProjectRepository Projects { get; } = new();
    public InMemoryMediaRepository Media { get; } = new();
    public FakeFfprobeExtractor Ffprobe { get; } = new();
    public FakeFreeSpaceProvider FreeSpace { get; } = new();
    public FakeThumbnailService Thumbnails { get; } = new();
    public FakePipelineJobManager? PipelineJobs { get; }
    public MediaImportService Service { get; }

    /// <summary>Cria o projeto com as quatro pastas obrigatórias.</summary>
    public Project CreateProject(string rootPath)
    {
        foreach (var folder in Project.RequiredFourFolders)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(rootPath, folder));
        }

        var project = new Project { ProjectId = Guid.NewGuid(), Name = "Projeto de teste", RootPath = rootPath };
        Projects.Projects.Add(project);
        return project;
    }

    public void Dispose()
    {
        // Sem recursos não gerenciados: o provedor de teste não possui estado a liberar.
    }

    public static StartImportRequest Request(
        Guid projectId,
        IReadOnlyList<string> paths,
        string destination = "1-Bruto",
        string? subfolder = null,
        bool copyFiles = true,
        bool generateThumbnails = false,
        bool analyzeMetadata = false,
        bool detectSpeech = false,
        IReadOnlyList<string>? tags = null)
        => new(projectId, paths, destination, subfolder, copyFiles, generateThumbnails, analyzeMetadata, detectSpeech, tags ?? Array.Empty<string>());

    public static async Task<ImportJobStatusDto> WaitForFinalAsync(MediaImportService service, Guid jobId, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(20));
        while (true)
        {
            var status = service.GetStatus(jobId) ?? throw new InvalidOperationException("Job desconhecido no teste.");
            if (status.State is "completed" or "completed_with_errors" or "failed" or "canceled")
                return status;

            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Importação {jobId} não terminou a tempo. Estado atual: {status.State}/{status.Stage}");

            await Task.Delay(15);
        }
    }
}

internal static class TestFiles
{
    public static byte[] RandomBytes(int length, int seed = 42)
    {
        var rng = new Random(seed);
        var bytes = new byte[length];
        rng.NextBytes(bytes);
        return bytes;
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string WriteSource(string path, byte[] data, DateTime? lastWriteUtc = null)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        File.SetLastWriteTimeUtc(path, lastWriteUtc ?? new DateTime(2024, 3, 1, 10, 0, 0, DateTimeKind.Utc));
        return path;
    }
}
