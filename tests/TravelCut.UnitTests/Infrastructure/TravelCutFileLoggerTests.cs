using Microsoft.Extensions.Logging;
using TravelCut.Infrastructure.Logging;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

/// <summary>
/// Log em arquivo do Desktop: mascaramento de perfil/segredos, rotação diária com 14 arquivos,
/// cabeçalho com versão/SO, nível mínimo e escrita concorrente sem perda dentro da capacidade da fila.
/// </summary>
public sealed class TravelCutFileLoggerTests : IDisposable
{
    private const string Profile = @"C:\Users\Fulano";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tc_log_" + Guid.NewGuid().ToString("N"));

    public TravelCutFileLoggerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Melhor esforço: o provider já foi descartado nos testes.
        }
    }

    [Fact]
    public void Redact_SubstituiPerfilPorUserProfileLiteral_InclusiveComBarraNormal()
    {
        string text = @"Falha em C:\Users\Fulano\Videos\clip.mp4 e C:/Users/Fulano/x.mov";

        string redacted = LogRedactor.Redact(text, Profile);

        Assert.Equal(@"Falha em %USERPROFILE%\Videos\clip.mp4 e %USERPROFILE%/x.mov", redacted);
        Assert.DoesNotContain("Fulano", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_IgnoraCaixaNoPerfil()
    {
        string redacted = LogRedactor.Redact(@"abrindo c:\users\FULANO\a.mp4", Profile);

        Assert.Equal(@"abrindo %USERPROFILE%\a.mp4", redacted);
    }

    [Fact]
    public void Redact_MascaraTokensSenhasEBearer()
    {
        string text = "token=abc123SECRET senha: minhaSenha Authorization: Bearer eyJhbGciOi.payload.sig " +
                      "\"api_key\":\"sk-live-999\" access_token=zzz refresh-token: yyy";

        string redacted = LogRedactor.Redact(text, Profile);

        Assert.DoesNotContain("abc123SECRET", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("minhaSenha", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOi", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-live-999", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("zzz", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("yyy", redacted, StringComparison.Ordinal);
        Assert.Contains(LogRedactor.Redacted, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Provider_GravaArquivoMascaradoComCabecalhoDeVersaoESo()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
        using (var provider = new TravelCutFileLoggerProvider(_dir, LogLevel.Information,
                   TravelCutFileLoggerProvider.BuildHeader("9.9.9-teste"), Profile, clock))
        {
            var logger = provider.CreateLogger("Teste");
            logger.LogInformation("Abrindo {Path}", @"C:\Users\Fulano\Projeto\1-Bruto\a.mp4");
            logger.LogWarning("Senha=hunter2 no cadastro");
        }

        string file = Assert.Single(Directory.GetFiles(_dir, "travelcut-*.log"));
        Assert.Equal("travelcut-" + clock.LocalDay() + ".log", Path.GetFileName(file));

        string content = File.ReadAllText(file);
        string[] lines = content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("TravelCut Studio 9.9.9-teste", lines[0]);
        Assert.Contains("SO:", lines[0]);
        Assert.Contains("%USERPROFILE%", content);
        Assert.DoesNotContain("Fulano", content, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotacao_MantemQuatorzeArquivosDiariosEPreservaOutrosArquivos()
    {
        // 16 arquivos diários antigos (2 além do limite) + um arquivo que não é do log.
        for (int day = 1; day <= 16; day++)
        {
            File.WriteAllText(Path.Combine(_dir, $"travelcut-202601{day:00}.log"), "antigo" + Environment.NewLine);
        }
        File.WriteAllText(Path.Combine(_dir, "outro.log"), "não é do app");

        var clock = new FakeClock(new DateTimeOffset(2026, 1, 20, 12, 0, 0, TimeSpan.Zero));
        using (var provider = new TravelCutFileLoggerProvider(_dir, LogLevel.Information, "cabecalho", Profile, clock))
        {
            // Poda na criação: os 2 mais antigos saem, 14 permanecem.
            Assert.Equal(14, DailyFiles().Count);
            Assert.False(File.Exists(Path.Combine(_dir, "travelcut-20260101.log")));
            Assert.False(File.Exists(Path.Combine(_dir, "travelcut-20260102.log")));

            // Mudança de dia cria arquivo novo e, pela mesma regra, remove o mais antigo.
            provider.CreateLogger("Teste").LogInformation("primeira mensagem do dia novo");
        }

        var names = DailyFiles();
        Assert.Equal(14, names.Count);
        Assert.Contains("travelcut-20260120.log", names);
        Assert.DoesNotContain("travelcut-20260103.log", names);
        Assert.Contains("travelcut-20260104.log", names);
        Assert.True(File.Exists(Path.Combine(_dir, "outro.log")), "arquivos fora do padrão não são apagados");
    }

    [Fact]
    public void Rotacao_CriaUmArquivoPorDiaLocal()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        using (var provider = new TravelCutFileLoggerProvider(_dir, LogLevel.Information, "cabecalho", Profile, clock))
        {
            var logger = provider.CreateLogger("Teste");
            logger.LogInformation("dia um");
            clock.Now = new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero);
            logger.LogInformation("dia dois");
        }

        Assert.Equal(2, DailyFiles().Count);
        Assert.Contains(File.ReadAllLines(Path.Combine(_dir, "travelcut-" + new FakeClock(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero)).LocalDay() + ".log")),
            l => l.Contains("dia um", StringComparison.Ordinal));
        Assert.Contains(File.ReadAllLines(Path.Combine(_dir, "travelcut-" + new FakeClock(new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero)).LocalDay() + ".log")),
            l => l.Contains("dia dois", StringComparison.Ordinal));
    }

    [Fact]
    public void NivelMinimo_DescartaMensagensAbaixoDeInformation()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 5, 5, 12, 0, 0, TimeSpan.Zero));
        using (var provider = new TravelCutFileLoggerProvider(_dir, LogLevel.Information, "cabecalho", Profile, clock))
        {
            var logger = provider.CreateLogger("Teste");
            Assert.False(logger.IsEnabled(LogLevel.Debug));
            logger.LogDebug("depuração que não deve aparecer");
            logger.LogInformation("informação que deve aparecer");
        }

        string content = File.ReadAllText(Path.Combine(_dir, "travelcut-" + clock.LocalDay() + ".log"));
        Assert.DoesNotContain("depuração", content, StringComparison.Ordinal);
        Assert.Contains("informação que deve aparecer", content, StringComparison.Ordinal);
    }

    [Fact]
    public void EscritaConcorrente_NaoPerdeLinhasDentroDaCapacidade()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 7, 7, 12, 0, 0, TimeSpan.Zero));
        const int threads = 8;
        const int perThread = 200;

        using (var provider = new TravelCutFileLoggerProvider(_dir, LogLevel.Information, "cabecalho", Profile, clock))
        {
            var logger = provider.CreateLogger("Concorrente");
            Parallel.For(0, threads, t =>
            {
                for (int i = 0; i < perThread; i++)
                {
                    logger.LogInformation("thread {T} item {I}", t, i);
                }
            });
        }

        string[] lines = File.ReadAllLines(Path.Combine(_dir, "travelcut-" + clock.LocalDay() + ".log"));
        // Cabeçalho + uma linha por mensagem; nenhuma mensagem fica pela metade.
        Assert.Equal(1 + threads * perThread, lines.Length);
        Assert.All(lines.Skip(1), l => Assert.Contains(" [INF] Concorrente: thread ", l, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("travelcut-20260101.log", true)]
    [InlineData("travelcut-2026010.log", false)]
    [InlineData("travelcut-20260101.log.bak", false)]
    [InlineData("outro-20260101.log", false)]
    public void IsDailyFileName_AceitaSomenteNomeDiarioExato(string name, bool expected)
    {
        Assert.Equal(expected, TravelCutFileLoggerProvider.IsDailyFileName(name));
    }

    private List<string> DailyFiles()
        => Directory.GetFiles(_dir, "travelcut-*.log").Select(Path.GetFileName).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal).ToList();

    private sealed class FakeClock : TimeProvider
    {
        public FakeClock(DateTimeOffset now) => Now = now;

        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;

        /// <summary>Data local no formato do nome do arquivo (yyyyMMdd).</summary>
        public string LocalDay() => Now.ToLocalTime().ToString("yyyyMMdd", global::System.Globalization.CultureInfo.InvariantCulture);
    }
}
