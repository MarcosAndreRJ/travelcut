using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using TravelCut.Application.Ipc;
using TravelCut.Application.UseCases;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;

namespace TravelCut.Desktop;

public partial class MainWindow : Window
{
    // DevTools e dev server (localhost:5173) existem só em DEBUG. Em Release, a origem confiável é apenas https://travelcut.studio.
    private static readonly bool AllowDevServer = DetermineAllowDevServer();

    private static bool DetermineAllowDevServer()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MainWindow> _logger;

    public MainWindow(IServiceProvider serviceProvider, ILogger<MainWindow> logger)
    {
        InitializeComponent();
        _serviceProvider = serviceProvider;
        _logger = logger;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await InitializeWebViewAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao inicializar WebView2");
            MessageBox.Show($"Falha ao inicializar WebView2: {ex.Message}", "Erro de Inicializacao", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task InitializeWebViewAsync()
    {
        string userDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TravelCutStudio", "WebView2");
        Directory.CreateDirectory(userDataDir);

        var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
        await WebViewControl.EnsureCoreWebView2Async(env);

        var core = WebViewControl.CoreWebView2;
        core.Settings.AreDevToolsEnabled = AllowDevServer;
#if !DEBUG
        // Release: sem menu de contexto nativo, sem atalhos do navegador (F5, Ctrl+P, Ctrl+U...) e sem barra de status.
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
#endif
        core.WebMessageReceived += CoreWebView2_WebMessageReceived;
        core.NavigationStarting += CoreWebView2_NavigationStarting;
        core.NewWindowRequested += CoreWebView2_NewWindowRequested;

        // Captura erros e unhandled rejections do JavaScript no frontend e encaminha para o log nativo
        await core.AddScriptToExecuteOnDocumentCreatedAsync(@"
            window.addEventListener('error', function(e) {
                try {
                    if (window.chrome && window.chrome.webview) {
                        window.chrome.webview.postMessage(JSON.stringify({
                            type: 'system.logClientError',
                            payload: {
                                message: e.message,
                                source: e.filename,
                                lineno: e.lineno,
                                colno: e.colno,
                                stack: e.error ? e.error.stack : null
                            }
                        }));
                    }
                } catch (_) {}
            });
            window.addEventListener('unhandledrejection', function(e) {
                try {
                    if (window.chrome && window.chrome.webview) {
                        window.chrome.webview.postMessage(JSON.stringify({
                            type: 'system.logClientError',
                            payload: {
                                message: e.reason ? (e.reason.message || String(e.reason)) : 'Unhandled rejection',
                                stack: e.reason && e.reason.stack ? e.reason.stack : null
                            }
                        }));
                    }
                } catch (_) {}
            });
        ");

        string? distDir = ResolveFrontendDist();
        if (distDir != null)
        {
            _logger.LogInformation("Mapeando virtual host seguro para frontend local: {Path}", distDir);

            // Mapeia https://travelcut.studio para o diretorio local de arquivos estaticos
            core.SetVirtualHostNameToFolderMapping(
                IpcOriginPolicy.PublishedHost,
                distDir,
                CoreWebView2HostResourceAccessKind.Allow);

            core.Navigate("https://travelcut.studio/index.html");
        }
        else if (AllowDevServer)
        {
            _logger.LogInformation("Dist nao encontrado. Tentando conectar ao Vite dev server ({Origin})", IpcOriginPolicy.DevServerOrigin);
            core.Navigate(IpcOriginPolicy.DevServerOrigin);
        }
        else
        {
            _logger.LogError("Frontend compilado não encontrado (frontend/dist/index.html) junto ao executável.");
            MessageBox.Show(
                "O frontend compilado não foi encontrado junto ao aplicativo (frontend/dist/index.html).\n" +
                "Reinstale o TravelCut Studio ou gere o frontend antes de compilar a versão de release.",
                "Frontend não encontrado",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        // Navegação inicial: ainda não há projeto aberto, então nenhum host de mídia fica mapeado.
        UpdateMediaHostMapping(null);

        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Host virtual de mídia da prévia (F07). Expõe SOMENTE a raiz do projeto aberto, em somente leitura
    /// (o WebView2 serve apenas GET) e com DenyCors. Cada troca limpa o mapeamento anterior.
    /// O host de mídia não é origem IPC confiável (IpcOriginPolicy) e não é destino de navegação de topo.
    /// Sem projeto (null): apenas limpa o mapeamento.
    /// </summary>
    private void UpdateMediaHostMapping(string? projectRoot)
    {
        var core = WebViewControl.CoreWebView2;
        if (core == null)
        {
            return;
        }

        try
        {
            core.ClearVirtualHostNameToFolderMapping(PreviewPaths.MediaHost);

            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return;
            }

            string fullRoot = Path.GetFullPath(projectRoot);
            if (!Directory.Exists(fullRoot))
            {
                _logger.LogWarning("Raiz do projeto inexistente: host de mídia não mapeado");
                return;
            }

            core.SetVirtualHostNameToFolderMapping(PreviewPaths.MediaHost, fullRoot, CoreWebView2HostResourceAccessKind.Allow);
            _logger.LogInformation("Host virtual de mídia mapeado para o projeto aberto (somente leitura)");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError(ex, "Falha ao mapear host virtual de mídia");
        }
    }

    /// <summary>Observa respostas de sucesso de project.open/project.create para atualizar o host de mídia.</summary>
    private void ApplyMediaHostForResponse(IpcResponse response)
    {
        if (!response.Ok)
        {
            return;
        }

        string? openedRoot = response.Payload switch
        {
            OpenProjectResponse open => open.Project.RootPath,
            CreateProjectResponse created => created.Project.RootPath,
            _ => null
        };

        if (openedRoot != null)
        {
            UpdateMediaHostMapping(openedRoot);
        }
    }

    /// <summary>
    /// Diretório com o index.html do frontend. Release usa apenas frontend/dist ao lado do executável.
    /// Em DEBUG, quando o build de desenvolvimento do repositório for mais recente, ele é preferido.
    /// </summary>
    private static string? ResolveFrontendDist()
    {
        string binDistDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "frontend", "dist");
        bool binAvailable = HasIndexHtml(binDistDir);

#if DEBUG
        string sourceDistDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "frontend", "dist"));
        if (HasIndexHtml(sourceDistDir)
            && (!binAvailable || File.GetLastWriteTimeUtc(Path.Combine(sourceDistDir, "index.html")) >= File.GetLastWriteTimeUtc(Path.Combine(binDistDir, "index.html"))))
        {
            return sourceDistDir;
        }
#endif

        return binAvailable ? binDistDir : null;
    }

    private static bool HasIndexHtml(string distDir) =>
        Directory.Exists(distDir) && File.Exists(Path.Combine(distDir, "index.html"));

    private const string VideoPatterns = "*.mp4;*.mov;*.m4v;*.mkv;*.avi;*.mts;*.m2ts";
    private const string AudioPatterns = "*.m4a;*.mp3;*.wav;*.aac;*.flac;*.ogg;*.wma";
    private const string ImagePatterns = "*.jpg;*.jpeg;*.png;*.heic;*.webp;*.tif;*.tiff";

    /// <summary>Filtro do seletor: "video" | "assets" (áudio e imagens) | null (todas as mídias).</summary>
    private static string? ReadSelectFilesFilter(object? payload)
    {
        if (payload is JsonElement je && je.ValueKind == JsonValueKind.Object
            && je.TryGetProperty("filter", out var filter) && filter.ValueKind == JsonValueKind.String)
        {
            return filter.GetString();
        }

        return null;
    }

    private static string BuildMediaFilter(string? filter)
    {
        return filter switch
        {
            "video" => $"Vídeos|{VideoPatterns}",
            "assets" => $"Áudio e imagens|{AudioPatterns};{ImagePatterns}",
            _ => $"Mídias|{VideoPatterns};{AudioPatterns};{ImagePatterns}|" +
                 $"Vídeos|{VideoPatterns}|Áudio|{AudioPatterns}|Imagens|{ImagePatterns}"
        };
    }

    /// <summary>Bloqueia navegação para qualquer origem fora da allowlist (https://travelcut.studio, e dev server só em DEBUG).</summary>
    private void CoreWebView2_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IpcOriginPolicy.IsTrustedOrigin(e.Uri, AllowDevServer))
        {
            return;
        }

        e.Cancel = true;
        _logger.LogWarning("Navegação bloqueada: destino fora da origem confiável do aplicativo");
    }

    /// <summary>Nenhuma janela nova: links que pedem outra janela não são abertos pelo aplicativo.</summary>
    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        _logger.LogInformation("Pedido de nova janela ignorado");
    }

    private async void CoreWebView2_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string rawMessage = string.Empty;
        try
        {
            // Mensagens só de origem confiável (o mesmo critério da navegação).
            if (!IpcOriginPolicy.IsTrustedOrigin(e.Source, AllowDevServer))
            {
                _logger.LogWarning("Mensagem IPC ignorada: origem não confiável");
                return;
            }

            rawMessage = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                return;
            }

            var request = JsonSerializer.Deserialize<IpcRequest>(rawMessage, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (request == null)
            {
                return;
            }

            if (request.Type == "system.logClientError")
            {
                _logger.LogError("Client JS Error reportado: {Payload}", JsonSerializer.Serialize(request.Payload));
                return;
            }

            if (request.Type == "dialog.selectFolder")
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Selecionar Pasta Raiz do Projeto",
                    Multiselect = false
                };

                bool? dialogResult = dialog.ShowDialog(this);
                if (dialogResult == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
                {
                    var resp = IpcResponse.Success(request.RequestId, new { selectedPath = dialog.FolderName, canceled = false });
                    WebViewControl.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(resp));
                }
                else
                {
                    var resp = IpcResponse.Success(request.RequestId, new { selectedPath = string.Empty, canceled = true });
                    WebViewControl.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(resp));
                }
                return;
            }

            if (request.Type == "dialog.selectFiles")
            {
                var selectRequest = ReadSelectFilesFilter(request.Payload);
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Selecionar mídia para importar",
                    Multiselect = true,
                    CheckFileExists = true,
                    Filter = BuildMediaFilter(selectRequest)
                };

                bool? dialogResult = dialog.ShowDialog(this);
                var paths = dialogResult == true ? dialog.FileNames.ToList() : new List<string>();
                var result = new SelectFilesResponse(paths, Canceled: dialogResult != true || paths.Count == 0);
                WebViewControl.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(IpcResponse.Success(request.RequestId, result)));
                return;
            }

            if (request.Type == "dnd.resolvePaths")
            {
                // Arquivos soltos no WebView chegam como CoreWebView2File (com Path) via postMessageWithAdditionalObjects.
                var droppedPaths = new List<string>();
                IEnumerable<object>? droppedObjects = e.AdditionalObjects;
                if (droppedObjects != null)
                {
                    foreach (object obj in droppedObjects)
                    {
                        if (obj is CoreWebView2File file && !string.IsNullOrWhiteSpace(file.Path))
                        {
                            droppedPaths.Add(file.Path);
                        }
                    }
                }

                var resp = IpcResponse.Success(request.RequestId, new { paths = droppedPaths });
                WebViewControl.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(resp));
                return;
            }

            // system.openFolder e demais métodos: o IpcDispatcher é a única implementação.
            using var scope = _serviceProvider.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IpcDispatcher>();
            var response = await dispatcher.DispatchAsync(request);

            // Antes de devolver a resposta: a UI já encontra o host de mídia do projeto recém-aberto.
            ApplyMediaHostForResponse(response);

            string responseJson = JsonSerializer.Serialize(response);
            WebViewControl.CoreWebView2.PostWebMessageAsJson(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no tratamento de mensagem IPC");
            string reqId = "unknown";
            try
            {
                using var doc = JsonDocument.Parse(rawMessage);
                if (doc.RootElement.TryGetProperty("requestId", out var rIdProp))
                {
                    reqId = rIdProp.GetString() ?? "unknown";
                }
            }
            catch { }

            // Sem mensagem de exceção para a UI: a referência liga a resposta ao log.
            var err = IpcResponse.Fail(reqId, "DISPATCHER_EXCEPTION", $"Erro interno (ref {reqId})");
            WebViewControl.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(err));
        }
    }
}
