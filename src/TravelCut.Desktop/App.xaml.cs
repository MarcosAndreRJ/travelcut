using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Infrastructure.Composition;
using TravelCut.Infrastructure.Logging;
using TravelCut.Infrastructure.Persistence;

namespace TravelCut.Desktop;

public partial class App : System.Windows.Application
{
    // Release registra Information; DEBUG registra também Debug.
#if DEBUG
    private const LogLevel FileLogLevel = LogLevel.Debug;
#else
    private const LogLevel FileLogLevel = LogLevel.Information;
#endif

    private ServiceProvider? _serviceProvider;
    private TravelCutFileLoggerProvider? _fileLogs;
    private ILogger _log = NullLogger.Instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Logger de arquivo antes de tudo: falhas da própria inicialização também ficam registradas.
        // Sem permissão/espaço para a pasta de logs o app continua (sem log em arquivo) em vez de cair sem diálogo.
        try
        {
            _fileLogs = new TravelCutFileLoggerProvider(
                TravelCutFileLoggerProvider.DefaultDirectory(),
                FileLogLevel,
                TravelCutFileLoggerProvider.BuildHeader(AppVersion()));
            _log = _fileLogs.CreateLogger("TravelCut.Desktop");
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _fileLogs = null;
            _log = NullLogger.Instance;
        }
        RegisterGlobalHandlers();

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services, _fileLogs);
            _serviceProvider = services.BuildServiceProvider();

            // Inicializar banco de dados SQLite e migrar colunas se necessário.
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TravelCutDbContext>();
                db.EnsureSchemaUpdated(_log);
            }

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            _log.LogCritical(ex, "Falha na inicialização do TravelCut Studio");
            MessageBox.Show(
                StartupFailureClassifier.UserMessage(ex, TravelCutFileLoggerProvider.DefaultDirectory()),
                "Falha ao iniciar TravelCut Studio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void ConfigureServices(IServiceCollection services, ILoggerProvider? fileLogs)
    {
        // Logging: console (Debug) e arquivo diário em %LOCALAPPDATA%\TravelCutStudio\logs.
        // O servidor MCP usa somente stderr.
        services.AddLogging(b =>
        {
            b.AddConsole();
            if (fileLogs is not null) b.AddProvider(fileLogs);
            b.SetMinimumLevel(LogLevel.Debug);
        });

        // Application Core compartilhado com o servidor MCP (banco, repositórios, editor, análise, IPC).
        // Banco: %LOCALAPPDATA%\TravelCutStudio\travelcut.db. TRAVELCUT_EDITOR_ADAPTER=mock → simulador.
        services.AddTravelCutCore(TravelCutHostOptions.FromEnvironment(AppContext.BaseDirectory));

        // Prévia de mídia (F07) fica no Desktop: depende do host virtual/WebView2 e alimenta o IpcDispatcher.
        services.AddTravelCutPreview();

        // Window (WPF)
        services.AddTransient<MainWindow>();
    }

    /// <summary>
    /// Erros não tratados: registra no log e mostra mensagem curta (sem stack trace).
    /// Erro na thread de UI é marcado como tratado para o app continuar; os demais seguem o runtime.
    /// </summary>
    private void RegisterGlobalHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ReportUnexpected("interface", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            ReportUnexpected("domínio", args.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ReportUnexpected("tarefa em segundo plano", args.Exception);
            args.SetObserved();
        };
    }

    private void ReportUnexpected(string origin, Exception? exception)
    {
        try
        {
            _log.LogError(exception, "Erro não tratado ({Origem})", origin);
        }
        catch (Exception)
        {
            // Log indisponível: segue para a mensagem.
        }

        string message = "Ocorreu um erro inesperado no TravelCut Studio.\n\n"
                         + $"Detalhe: {ShortDescription(exception)}.\n\n"
                         + $"Log: {TravelCutFileLoggerProvider.DefaultDirectory()}";
        try
        {
            if (Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => ShowMessage(message)));
            }
            else
            {
                ShowMessage(message);
            }
        }
        catch (Exception)
        {
            // Sem dispatcher (encerramento): o log já registrou o erro.
        }
    }

    private static void ShowMessage(string message)
        => MessageBox.Show(message, "TravelCut Studio", MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>Tipo e primeira linha da mensagem: sem stack trace.</summary>
    private static string ShortDescription(Exception? exception)
    {
        if (exception == null) return "falha desconhecida";
        string firstLine = (exception.Message ?? string.Empty).Split('\n', 2)[0].Trim();
        return string.IsNullOrEmpty(firstLine)
            ? exception.GetType().Name
            : $"{exception.GetType().Name}: {firstLine}";
    }

    private static string AppVersion()
        => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
           ?? "desconhecida";

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        _fileLogs?.Dispose();
        base.OnExit(e);
    }
}
