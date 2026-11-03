using TravelCut.Infrastructure.Logging;
using Xunit;

namespace TravelCut.UnitTests.Infrastructure;

/// <summary>A1: causa da falha de inicialização traduzida para português; mensagem sem stack trace.</summary>
public class StartupFailureClassifierTests
{
    [Fact]
    public void SchemaTooNew_ViraBancoDeVersaoMaisNova()
    {
        var ex = new InvalidOperationException(StartupFailureClassifier.SchemaTooNew);

        Assert.Equal("banco criado por versão mais nova do TravelCut", StartupFailureClassifier.Describe(ex));
    }

    [Fact]
    public void SchemaMigrationInProgress_ViraOutraInstancia()
    {
        var ex = new InvalidOperationException(StartupFailureClassifier.SchemaMigrationInProgress);

        Assert.Equal("outra instância (app ou MCP) está atualizando o banco", StartupFailureClassifier.Describe(ex));
    }

    [Fact]
    public void DatabaseLocked_EmCadeiaDeInnerException()
    {
        var ex = new InvalidOperationException("falha ao migrar", new Exception("SQLite Error 5: 'database is locked'."));

        Assert.Equal("o banco está em uso por outro processo (database is locked)", StartupFailureClassifier.Describe(ex));
    }

    [Fact]
    public void DiscoCheio_PorMensagemDoSqlite()
    {
        Assert.Equal("disco cheio", StartupFailureClassifier.Describe(new Exception("SQLite Error 13: 'database or disk is full'.")));
    }

    [Fact]
    public void DiscoCheio_PorHResultDeIO()
    {
        var ex = new IOException("erro de escrita", unchecked((int)0x80070070));

        Assert.Equal("disco cheio", StartupFailureClassifier.Describe(ex));
    }

    [Fact]
    public void Generico_QuandoNaoReconhecido()
    {
        Assert.Equal("erro inesperado na inicialização", StartupFailureClassifier.Describe(new InvalidOperationException("algo estranho")));
        Assert.Equal("erro inesperado na inicialização", StartupFailureClassifier.Describe(null));
    }

    [Fact]
    public void MensagemDoUsuario_TemCausaELocalDoLog_SemStackTrace()
    {
        var ex = new InvalidOperationException(StartupFailureClassifier.SchemaTooNew);
        string text = StartupFailureClassifier.UserMessage(ex, @"C:\Users\Ana\AppData\Local\TravelCutStudio\logs");

        Assert.Contains("versão mais nova", text, StringComparison.Ordinal);
        Assert.Contains(@"TravelCutStudio\logs", text, StringComparison.Ordinal);
        Assert.DoesNotContain("   em ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("at ", text, StringComparison.Ordinal);
    }
}
