using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Media.Thumbnails;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

/// <summary>
/// M6: no máximo 2 FFmpegs de miniatura simultâneos no processo inteiro (semáforo estático),
/// e entrada de imagem com prefixo "file:" (nomes com % não viram padrão).
/// Runner falso lento: nenhum FFmpeg real é executado.
/// </summary>
public sealed class ThumbnailConcurrencyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tc_thumb_" + Guid.NewGuid().ToString("N"));

    public ThumbnailConcurrencyTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Melhor esforço.
        }
    }

    [Fact]
    public async Task GeracaoDeMiniaturas_NoMaximoDuasSimultaneas()
    {
        int running = 0;
        int maxRunning = 0;
        var service = new FfmpegThumbnailService(NullLogger<FfmpegThumbnailService>.Instance, () => @"C:\falso\ffmpeg.exe",
            async (ffmpeg, input, at, output, ct) =>
            {
                int now = Interlocked.Increment(ref running);
                InterlockedMax(ref maxRunning, now);
                try
                {
                    await Task.Delay(250, ct);
                    await File.WriteAllBytesAsync(output, new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 }, ct);
                    return (0, false);
                }
                finally
                {
                    Interlocked.Decrement(ref running);
                }
            });

        var media = CreateMediaFiles(6);
        var results = await Task.WhenAll(media.Select(m => service.GenerateAsync(_root, m, 0.0)));

        Assert.All(results, r => Assert.Equal(ThumbnailStatus.Ok, r.Status));
        Assert.True(maxRunning <= FfmpegThumbnailService.MaxConcurrentGenerations, $"máximo observado: {maxRunning}");
        Assert.Equal(FfmpegThumbnailService.MaxConcurrentGenerations, maxRunning);
    }

    [Fact]
    public async Task DuasInstancias_DividemAsMesmasDuasVagas()
    {
        int running = 0;
        int maxRunning = 0;
        FfmpegThumbnailService.ThumbnailProcessRunner runner = async (ffmpeg, input, at, output, ct) =>
        {
            int now = Interlocked.Increment(ref running);
            InterlockedMax(ref maxRunning, now);
            try
            {
                await Task.Delay(200, ct);
                await File.WriteAllBytesAsync(output, new byte[] { 0xFF, 0xD8, 0xFF, 1 }, ct);
                return (0, false);
            }
            finally
            {
                Interlocked.Decrement(ref running);
            }
        };
        var first = new FfmpegThumbnailService(NullLogger<FfmpegThumbnailService>.Instance, () => @"C:\falso\ffmpeg.exe", runner);
        var second = new FfmpegThumbnailService(NullLogger<FfmpegThumbnailService>.Instance, () => @"C:\falso\ffmpeg.exe", runner);

        var media = CreateMediaFiles(4);
        var tasks = media.Select((m, i) => (i % 2 == 0 ? first : second).GenerateAsync(_root, m, 0.0)).ToArray();
        await Task.WhenAll(tasks);

        Assert.True(maxRunning <= FfmpegThumbnailService.MaxConcurrentGenerations, $"máximo observado: {maxRunning}");
    }

    [Theory]
    [InlineData(@"C:\midia\foto 100%.jpg", "file:C:\\midia\\foto 100%.jpg")]
    [InlineData(@"C:\midia\pasta%01d\img.PNG", "file:C:\\midia\\pasta%01d\\img.PNG")]
    [InlineData(@"C:\midia\clip.mp4", @"C:\midia\clip.mp4")]
    public void BuildInputArgument_PrefixaFileSomenteParaImagens(string path, string expected)
    {
        Assert.Equal(expected, FfmpegThumbnailService.BuildInputArgument(path));
    }

    private List<string> CreateMediaFiles(int count)
    {
        var paths = new List<string>();
        for (int i = 0; i < count; i++)
        {
            string path = Path.Combine(_root, $"foto_{i}.jpg");
            File.WriteAllBytes(path, new byte[] { 0xFF, 0xD8, (byte)i, 0xFF });
            paths.Add(path);
        }
        return paths;
    }

    private static void InterlockedMax(ref int target, int candidate)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref target);
            if (candidate <= seen) return;
        }
        while (Interlocked.CompareExchange(ref target, candidate, seen) != seen);
    }
}
