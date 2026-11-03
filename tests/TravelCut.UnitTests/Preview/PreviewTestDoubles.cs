using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Infrastructure.Media.Preview;

namespace TravelCut.UnitTests.Preview;

internal sealed class FakeProjectRepository : IProjectRepository
{
    public Dictionary<Guid, Project> Items { get; } = new();

    public Task<Project?> GetByIdAsync(Guid projectId, CancellationToken ct = default) =>
        Task.FromResult(Items.TryGetValue(projectId, out var p) ? p : null);

    public Task<Project?> GetByRootPathAsync(string rootPath, CancellationToken ct = default) =>
        Task.FromResult<Project?>(null);

    public Task<List<Project>> ListRecentAsync(int limit = 10, CancellationToken ct = default) =>
        Task.FromResult(Items.Values.Take(limit).ToList());

    public Task SaveAsync(Project project, CancellationToken ct = default)
    {
        Items[project.ProjectId] = project;
        return Task.CompletedTask;
    }
}

internal sealed class FakeMediaRepository : IMediaRepository
{
    public Dictionary<Guid, MediaAsset> Items { get; } = new();

    public Task<MediaAsset?> GetByIdAsync(Guid mediaId, CancellationToken ct = default) =>
        Task.FromResult(Items.TryGetValue(mediaId, out var m) ? m : null);

    public Task<List<MediaAsset>> ListByProjectAsync(Guid projectId, CancellationToken ct = default) =>
        Task.FromResult(Items.Values.Where(m => m.ProjectId == projectId).ToList());

    public Task SaveAsync(MediaAsset asset, CancellationToken ct = default)
    {
        Items[asset.MediaId] = asset;
        return Task.CompletedTask;
    }

    public Task SaveBatchAsync(IEnumerable<MediaAsset> assets, CancellationToken ct = default)
    {
        foreach (var a in assets) Items[a.MediaId] = a;
        return Task.CompletedTask;
    }
}

internal sealed class FakeFfmpegPath : IFfmpegPathProvider
{
    public string? Path { get; set; }

    public string? GetFfmpegPath() => Path;
}

/// <summary>Runner binário falso: entrega bytes configurados ao consumidor e registra as chamadas.</summary>
internal sealed class FakeBinaryRunner : IBinaryStdoutProcessRunner
{
    public List<ProcessRunRequest> Calls { get; } = new();
    public byte[] Stdout { get; set; } = Array.Empty<byte>();
    public ProcessRunStatus Status { get; set; } = ProcessRunStatus.Completed;
    public int ExitCode { get; set; }

    public async Task<BinaryStdoutRunResult> RunAsync(
        ProcessRunRequest request,
        Func<Stream, CancellationToken, Task> readStdout,
        CancellationToken ct = default)
    {
        Calls.Add(request);

        if (Status == ProcessRunStatus.NotStarted)
            return new BinaryStdoutRunResult(ProcessRunStatus.NotStarted, null, string.Empty, "não iniciado (teste)");

        if (Status == ProcessRunStatus.TimedOut)
            return new BinaryStdoutRunResult(ProcessRunStatus.TimedOut, null, string.Empty);

        using var ms = new MemoryStream(Stdout, writable: false);
        await readStdout(ms, ct);
        return new BinaryStdoutRunResult(Status, ExitCode, string.Empty);
    }
}

/// <summary>Runner falso de texto para o job de proxy. Por padrão, grava um arquivo no caminho de saída (último argumento).</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<ProcessRunRequest> Calls { get; } = new();
    public Func<ProcessRunRequest, CancellationToken, Task<ProcessRunResult>>? Handler { get; set; }

    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken ct = default)
    {
        Calls.Add(request);

        if (Handler != null)
            return await Handler(request, ct);

        string output = request.Arguments[^1];
        await File.WriteAllBytesAsync(output, new byte[] { 1, 2, 3, 4 }, ct);
        return new ProcessRunResult(ProcessRunStatus.Completed, 0, string.Empty, string.Empty);
    }
}

/// <summary>Projeto temporário com as quatro pastas canônicas, apagado no Dispose.</summary>
internal sealed class Harness : IDisposable
{
    public string Root { get; }
    public Project Project { get; }
    public FakeProjectRepository Projects { get; } = new();
    public FakeMediaRepository Media { get; } = new();
    public FakeFfmpegPath Ffmpeg { get; } = new();
    public FakeBinaryRunner Binary { get; } = new();
    public FakeProcessRunner Process { get; } = new();
    public FileWaveformCache Cache { get; } = new();
    public ProxyJobService Jobs { get; }
    public PreviewUseCases UseCases { get; }

    public Harness()
    {
        Root = Path.Combine(Path.GetTempPath(), "tc-preview-" + Guid.NewGuid().ToString("N"));
        foreach (var folder in new[] { "1-Bruto", "2-Assets", "3-Trabalho", "4-Final" })
        {
            Directory.CreateDirectory(Path.Combine(Root, folder));
        }

        Project = new Project { ProjectId = Guid.NewGuid(), Name = "Teste", RootPath = Root };
        Projects.Items[Project.ProjectId] = Project;

        Jobs = new ProxyJobService(Process, Ffmpeg, NullLogger<ProxyJobService>.Instance);
        UseCases = new PreviewUseCases(
            Projects, Media, Ffmpeg, Binary, Cache, Jobs, NullLogger<PreviewUseCases>.Instance);
    }

    /// <summary>Cria o asset no repositório e (opcionalmente) o arquivo sintético no disco.</summary>
    public MediaAsset AddMedia(
        string relativePath,
        string fingerprint = "fp-1",
        MediaKind kind = MediaKind.Video,
        MediaMetadata? metadata = null,
        bool writeFile = true,
        byte[]? content = null,
        Guid? mediaId = null)
    {
        var media = new MediaAsset
        {
            MediaId = mediaId ?? Guid.NewGuid(),
            ProjectId = Project.ProjectId,
            RelativePath = relativePath,
            Fingerprint = fingerprint,
            Kind = kind,
            Metadata = metadata
        };
        Media.Items[media.MediaId] = media;

        if (writeFile)
        {
            string full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, content ?? new byte[] { 0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p' });
        }

        return media;
    }

    public string FullPath(string relativePath) =>
        Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public string ProxiesFolder =>
        Path.Combine(Root, "3-Trabalho", "cache", "proxies");

    public static MediaMetadata OkMetadata(Guid mediaId, string codec, double durationSeconds = 10, int audioChannels = 2) => new()
    {
        MediaId = mediaId,
        Codec = codec,
        DurationSeconds = durationSeconds,
        AudioChannels = audioChannels,
        MetadataStatus = MediaMetadataStatus.Ok
    };

    public void Dispose()
    {
        Jobs.Dispose();
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal static class PcmBytes
{
    /// <summary>PCM s16le little-endian.</summary>
    public static byte[] S16(params short[] samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            bytes[i * 2] = (byte)(samples[i] & 0xFF);
            bytes[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
        }

        return bytes;
    }
}
