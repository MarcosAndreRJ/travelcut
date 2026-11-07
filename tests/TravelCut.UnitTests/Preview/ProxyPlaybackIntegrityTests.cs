using System.Text;
using TravelCut.Application.Ports;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Infrastructure.Media.Preview;
using Xunit;

namespace TravelCut.UnitTests.Preview;

public class ProxyPlaybackIntegrityTests
{
    private static byte[] CreateMockMp4Bytes(int size = 2048)
    {
        var bytes = new byte[size];
        // Simula cabeçalho MP4 válido: box ftyp (offset 4..7 = 'f','t','y','p')
        bytes[3] = 32;
        bytes[4] = (byte)'f';
        bytes[5] = (byte)'t';
        bytes[6] = (byte)'y';
        bytes[7] = (byte)'p';
        return bytes;
    }

    private static ProxyJobItemPlan Plan(Harness h, MediaAsset media) =>
        new(media.MediaId, Path.GetFileName(media.RelativePath), h.FullPath(media.RelativePath), media.Fingerprint, null);

    private static async Task<ProxyJobSnapshot> WaitTerminalAsync(IProxyJobService service, Guid jobId)
    {
        for (var i = 0; i < 200; i++)
        {
            var snap = service.GetJob(jobId);
            if (snap != null && snap.State is "completed" or "completed_with_errors" or "failed" or "canceled" or "ffmpeg_unavailable")
                return snap;
            await Task.Delay(20);
        }
        throw new TimeoutException("Job de proxy não terminou no tempo esperado.");
    }

    [Fact]
    public async Task ValidMp4Proxy_GeneratesAndValidatesSuccessfully()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";

        var media = h.AddMedia("1-Bruto/cena1.mp4", fingerprint: "fp-valid");
        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, media) });
        Assert.False(outcome.Conflict);

        var finished = await WaitTerminalAsync(h.Jobs, outcome.Job!.JobId);
        Assert.Equal("completed", finished.State);
        Assert.Equal(1, finished.Processed);

        var item = Assert.Single(finished.Items);
        Assert.Equal("ready", item.State);
        Assert.Contains("validado", item.Message);

        string finalPath = Path.Combine(h.ProxiesFolder, PreviewPaths.ProxyFileName(media.MediaId, "fp-valid"));
        Assert.True(File.Exists(finalPath));
    }

    [Fact]
    public async Task EmptyFile_FailsValidationAndIsDeleted()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var media = h.AddMedia("1-Bruto/cena2.mp4", fingerprint: "fp-empty");

        // Simula FFmpeg gerando arquivo vazio de 0 bytes
        h.Process.Handler = async (req, ct) =>
        {
            string outPath = req.Arguments[^1];
            if (!outPath.EndsWith("-"))
            {
                await File.WriteAllBytesAsync(outPath, Array.Empty<byte>(), ct);
            }
            return new ProcessRunResult(ProcessRunStatus.Completed, 0, string.Empty, string.Empty);
        };

        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, media) });
        var finished = await WaitTerminalAsync(h.Jobs, outcome.Job!.JobId);

        Assert.Equal("failed", finished.State);
        var item = Assert.Single(finished.Items);
        Assert.Equal("failed", item.State);

        string finalPath = Path.Combine(h.ProxiesFolder, PreviewPaths.ProxyFileName(media.MediaId, "fp-empty"));
        Assert.False(File.Exists(finalPath));
        Assert.Empty(Directory.GetFiles(h.ProxiesFolder, "*.tcpart.mp4"));
    }

    [Fact]
    public async Task InvalidMp4Header_MissingFtyp_FailsValidationAndIsDeleted()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var media = h.AddMedia("1-Bruto/cena3.mp4", fingerprint: "fp-bad-header");

        // Simula arquivo sem ftyp (corrompido)
        h.Process.Handler = async (req, ct) =>
        {
            string outPath = req.Arguments[^1];
            if (!outPath.EndsWith("-"))
            {
                var corrupted = new byte[2048]; // Tudo zeros, sem "ftyp"
                await File.WriteAllBytesAsync(outPath, corrupted, ct);
            }
            return new ProcessRunResult(ProcessRunStatus.Completed, 0, string.Empty, string.Empty);
        };

        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, media) });
        var finished = await WaitTerminalAsync(h.Jobs, outcome.Job!.JobId);

        Assert.Equal("failed", finished.State);
        var item = Assert.Single(finished.Items);
        Assert.Equal("failed", item.State);
        Assert.Contains("inválido", item.Message);

        string finalPath = Path.Combine(h.ProxiesFolder, PreviewPaths.ProxyFileName(media.MediaId, "fp-bad-header"));
        Assert.False(File.Exists(finalPath));
        Assert.Empty(Directory.GetFiles(h.ProxiesFolder, "*.tcpart.mp4"));
    }

    [Fact]
    public async Task DecodeFrameFailure_FailsValidationAndIsDeleted()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var media = h.AddMedia("1-Bruto/cena4.mp4", fingerprint: "fp-decode-fail");

        // Simula FFmpeg decodificando com erro ao validar o primeiro frame
        h.Process.Handler = async (req, ct) =>
        {
            if (req.Arguments.Contains("-f") && req.Arguments.Contains("null"))
            {
                // Comando de validação de decodificação falha
                return new ProcessRunResult(ProcessRunStatus.Completed, 1, string.Empty, "Erro de decodificação de frame");
            }
            string outPath = req.Arguments[^1];
            await File.WriteAllBytesAsync(outPath, CreateMockMp4Bytes(), ct);
            return new ProcessRunResult(ProcessRunStatus.Completed, 0, string.Empty, string.Empty);
        };

        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, media) });
        var finished = await WaitTerminalAsync(h.Jobs, outcome.Job!.JobId);

        Assert.Equal("failed", finished.State);
        var item = Assert.Single(finished.Items);
        Assert.Equal("failed", item.State);

        string finalPath = Path.Combine(h.ProxiesFolder, PreviewPaths.ProxyFileName(media.MediaId, "fp-decode-fail"));
        Assert.False(File.Exists(finalPath));
    }

    [Fact]
    public async Task InvalidCache_IsDetectedAndRemoved_AllowingRegeneration()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var media = h.AddMedia("1-Bruto/cena5.mp4", fingerprint: "fp-cache-corrupt");

        Directory.CreateDirectory(h.ProxiesFolder);
        string finalPath = Path.Combine(h.ProxiesFolder, PreviewPaths.ProxyFileName(media.MediaId, "fp-cache-corrupt"));
        // Cria arquivo corrompido no cache
        await File.WriteAllBytesAsync(finalPath, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });

        // Quando o job roda, ele deve detectar o cache inválido, apagar e regenerar
        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, media) });
        var finished = await WaitTerminalAsync(h.Jobs, outcome.Job!.JobId);

        Assert.Equal("completed", finished.State);
        var item = Assert.Single(finished.Items);
        Assert.Equal("ready", item.State);

        // O arquivo foi regenerado e agora contém os bytes válidos (com ftyp)
        byte[] regenerated = await File.ReadAllBytesAsync(finalPath);
        Assert.True(regenerated.Length >= 1024);
        Assert.Equal((byte)'f', regenerated[4]);
        Assert.Equal((byte)'t', regenerated[5]);
        Assert.Equal((byte)'y', regenerated[6]);
        Assert.Equal((byte)'p', regenerated[7]);
    }

    [Fact]
    public async Task PreviewUseCases_GetPlaybackSource_WithCorruptProxy_DoesNotReturnReadyProxy()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        var media = h.AddMedia("1-Bruto/cena6.mp4", fingerprint: "fp-puc-corrupt");

        Directory.CreateDirectory(h.ProxiesFolder);
        string finalPath = Path.Combine(h.ProxiesFolder, PreviewPaths.ProxyFileName(media.MediaId, "fp-puc-corrupt"));
        // Grava arquivo corrompido sem cabeçalho MP4 válido
        await File.WriteAllBytesAsync(finalPath, new byte[512]);

        var sourceResult = await h.UseCases.GetPlaybackSourceAsync(new PlaybackSourceRequest(h.Project.ProjectId, media.MediaId));
        Assert.True(sourceResult.Ok);
        Assert.NotNull(sourceResult.Value);

        // Não deve entregar como proxy pronto!
        Assert.NotEqual("proxy", sourceResult.Value.Status);
        Assert.NotEqual("ready", sourceResult.Value.ProxyStatus);
    }

    [Fact]
    public void PreviewPaths_VirtualHostMediaUrl_UsesConfiguredHost()
    {
        string url = PreviewPaths.BuildMediaUrl("3-Trabalho/cache/proxies/prx_teste.mp4");
        Assert.StartsWith("https://media.travelcut.studio/", url);
        Assert.Equal("https://media.travelcut.studio/3-Trabalho/cache/proxies/prx_teste.mp4", url);
    }

    [Fact]
    public async Task OriginalFile_IsNeverModifiedOrTouchedDuringProxyGeneration()
    {
        using var h = new Harness();
        h.Ffmpeg.Path = @"C:\ffmpeg\bin\ffmpeg.exe";
        byte[] originalContent = Encoding.UTF8.GetBytes("VIDEO_ORIGINAL_PRESERVADO_INTEGRO");
        var media = h.AddMedia("1-Bruto/original.mp4", fingerprint: "fp-orig", content: originalContent);

        string origPath = h.FullPath(media.RelativePath);

        var outcome = h.Jobs.Start(h.Project.ProjectId, h.Root, new[] { Plan(h, media) });
        var finished = await WaitTerminalAsync(h.Jobs, outcome.Job!.JobId);
        Assert.Equal("completed", finished.State);

        // Verifica que o conteúdo original não sofreu nenhuma alteração
        byte[] readBack = await File.ReadAllBytesAsync(origPath);
        Assert.Equal(originalContent, readBack);
    }
}
