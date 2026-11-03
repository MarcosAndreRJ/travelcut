using TravelCut.Application.Services;
using TravelCut.Infrastructure.Media;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

/// <summary>
/// M8: indisponibilidade do ffprobe expira em 30 s (disponibilidade confirmada fica em cache) e o localizador
/// segue a mesma política do FFmpeg (configurado, pasta do ffmpeg, locais comuns, PATH) com caminho absoluto.
/// </summary>
public sealed class FfprobeAvailabilityAndLocatorTests
{
    [Fact]
    public void IndisponibilidadeExpiraAposTrintaSegundos_DisponibilidadeConfirmadaFicaEmCache()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        var extractor = new ScriptedProbeExtractor(clock, false, true);

        Assert.False(extractor.IsFfprobeAvailable());
        Assert.Equal(1, extractor.Probes);

        // Dentro dos 30 s: nova consulta não executa o processo.
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(extractor.IsFfprobeAvailable());
        Assert.Equal(1, extractor.Probes);

        // Após 30 s: reavalia e encontra o ffprobe.
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(extractor.IsFfprobeAvailable());
        Assert.Equal(2, extractor.Probes);

        // Disponível: permanece em cache mesmo após muito tempo.
        clock.Advance(TimeSpan.FromHours(1));
        Assert.True(extractor.IsFfprobeAvailable());
        Assert.Equal(2, extractor.Probes);
    }

    [Fact]
    public void Resolve_CaminhoConfiguradoExistenteTemPrioridade()
    {
        string configured = @"C:\tools\ffprobe.exe";

        string? result = FfprobeLocator.Resolve(
            configured: configured,
            ffmpegPath: @"C:\outro\ffmpeg.exe",
            fileExists: p => p.Equals(configured, StringComparison.OrdinalIgnoreCase) || p.EndsWith(@"outro\ffprobe.exe", StringComparison.OrdinalIgnoreCase),
            pathDirectories: Array.Empty<string>(),
            commonWindowsLocations: Array.Empty<string>(),
            isWindows: true);

        Assert.Equal(configured, result);
    }

    [Fact]
    public void Resolve_SemConfiguracao_usaFfprobeAoLadoDoFfmpeg()
    {
        string sibling = @"C:\ffmpeg\bin\ffprobe.exe";

        string? result = FfprobeLocator.Resolve(
            configured: null,
            ffmpegPath: @"C:\ffmpeg\bin\ffmpeg.exe",
            fileExists: p => p.Equals(sibling, StringComparison.OrdinalIgnoreCase),
            pathDirectories: new[] { @"D:\qualquer" },
            commonWindowsLocations: Array.Empty<string>(),
            isWindows: true);

        Assert.Equal(sibling, result);
    }

    [Fact]
    public void Resolve_CaminhoRelativoViraAbsoluto()
    {
        string? result = FfprobeLocator.Resolve(
            configured: @"tools\ffprobe.exe",
            ffmpegPath: null,
            fileExists: p => p.EndsWith(@"ffprobe.exe", StringComparison.OrdinalIgnoreCase),
            pathDirectories: Array.Empty<string>(),
            commonWindowsLocations: Array.Empty<string>(),
            isWindows: true);

        Assert.NotNull(result);
        Assert.True(Path.IsPathFullyQualified(result));
    }

    [Fact]
    public void Resolve_ProcuraNoPathQuandoNadaMaisExiste()
    {
        string onPath = @"D:\bin\ffprobe.exe";

        string? result = FfprobeLocator.Resolve(
            configured: @"C:\nao\existe\ffprobe.exe",
            ffmpegPath: null,
            fileExists: p => p.Equals(onPath, StringComparison.OrdinalIgnoreCase),
            pathDirectories: new[] { @"C:\vazio", @"D:\bin" },
            commonWindowsLocations: Array.Empty<string>(),
            isWindows: true);

        Assert.Equal(onPath, result);
    }

    [Fact]
    public void Resolve_NadaEncontrado_retornaNulo()
    {
        string? result = FfprobeLocator.Resolve(
            configured: null,
            ffmpegPath: null,
            fileExists: _ => false,
            pathDirectories: new[] { @"C:\vazio" },
            commonWindowsLocations: new[] { @"C:\ffmpeg\bin\ffprobe.exe" },
            isWindows: true);

        Assert.Null(result);
    }

    /// <summary>Extrator com probe roteirizado: cada chamada devolve o próximo valor da sequência.</summary>
    private sealed class ScriptedProbeExtractor : FfprobeMetadataExtractor
    {
        private readonly Queue<bool> _results;

        public ScriptedProbeExtractor(TimeProvider clock, params bool[] results)
            : base(@"C:\inexistente\ffprobe.exe", timeProvider: clock)
        {
            _results = new Queue<bool>(results);
        }

        public int Probes { get; private set; }

        protected override bool Probe()
        {
            Probes++;
            return _results.Dequeue();
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualClock(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
