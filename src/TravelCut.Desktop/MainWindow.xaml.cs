using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using TravelCut.Application.Ipc;
using TravelCut.Application.Services;
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
    private string? _currentProjectRoot;

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
        core.WebResourceResponseReceived += CoreWebView2_WebResourceResponseReceived;

        // Filtro de segurança para o host de mídia: impede leitura arbitrária de arquivos do projeto fora de 1-Bruto e cache
        core.AddWebResourceRequestedFilter("https://media.travelcut.studio/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += CoreWebView2_MediaResourceRequested;

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
        if (!response.Ok || response.Payload == null)
        {
            return;
        }

        string? openedRoot = null;
        if (response.Payload is OpenProjectResponse open)
        {
            openedRoot = open.Project.RootPath;
        }
        else if (response.Payload is CreateProjectResponse created)
        {
            openedRoot = created.Project.RootPath;
        }
        else if (response.Payload is JsonElement json)
        {
            if (json.TryGetProperty("project", out var projProp) && projProp.TryGetProperty("rootPath", out var rp))
            {
                openedRoot = rp.GetString();
            }
            else if (json.TryGetProperty("rootPath", out var rootProp))
            {
                openedRoot = rootProp.GetString();
            }
        }
        else
        {
            try
            {
                var doc = JsonSerializer.SerializeToElement(response.Payload);
                if (doc.TryGetProperty("project", out var projProp) && projProp.TryGetProperty("rootPath", out var rp))
                {
                    openedRoot = rp.GetString();
                }
                else if (doc.TryGetProperty("rootPath", out var rootProp))
                {
                    openedRoot = rootProp.GetString();
                }
            }
            catch
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(openedRoot))
        {
            _currentProjectRoot = openedRoot;
            UpdateMediaHostMapping(openedRoot);
            _logger.LogInformation("Raiz de projeto ativa para streaming de mídia: {Path}", openedRoot);
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

    /// <summary>Logs de rede estruturados para o host virtual de mídia (diagnóstico de reprodução e Range/CORS).</summary>
    private void CoreWebView2_WebResourceResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            string uri = e.Request.Uri;
            if (!uri.StartsWith(PreviewPaths.MediaOrigin, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string method = e.Request.Method;
            string? range = null;
            if (e.Request.Headers.Contains("Range"))
            {
                range = e.Request.Headers.GetHeader("Range");
            }

            int status = e.Response?.StatusCode ?? 0;
            string reason = e.Response?.ReasonPhrase ?? string.Empty;
            string? contentType = null;
            string? contentLength = null;
            string? contentRange = null;

            if (e.Response?.Headers != null)
            {
                if (e.Response.Headers.Contains("Content-Type")) contentType = e.Response.Headers.GetHeader("Content-Type");
                if (e.Response.Headers.Contains("Content-Length")) contentLength = e.Response.Headers.GetHeader("Content-Length");
                if (e.Response.Headers.Contains("Content-Range")) contentRange = e.Response.Headers.GetHeader("Content-Range");
            }

            if (status >= 400 || status == 0)
            {
                _logger.LogWarning(
                    "WebView2 Media HTTP Falha: Method={Method}, URI={Uri}, Status={Status} {Reason}, Range={Range}, ContentType={ContentType}",
                    method, uri, status, reason, range ?? "(none)", contentType ?? "(none)");
            }
            else
            {
                _logger.LogInformation(
                    "WebView2 Media HTTP OK: Method={Method}, URI={Uri}, Status={Status} {Reason}, Range={Range}, ContentRange={ContentRange}, ContentType={ContentType}, ContentLength={ContentLength}",
                    method, uri, status, reason, range ?? "(none)", contentRange ?? "(none)", contentType ?? "(none)", contentLength ?? "(none)");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Erro ao registrar telemetria de rede WebView2");
        }
    }

    /// <summary>
    /// Servidor de mídia embutido para https://media.travelcut.studio/* com suporte real a HTTP 206 Partial Content,
    /// cabeçalho Range, controle de CORS restrito à origem confiável do app e isolamento estrito de caminho.
    /// </summary>
    private void CoreWebView2_MediaResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var core = WebViewControl?.CoreWebView2;
        if (core == null)
        {
            return;
        }

        try
        {
            var uri = new Uri(e.Request.Uri);
            string method = e.Request.Method.ToUpperInvariant();
            string allowedOrigin = "https://" + IpcOriginPolicy.PublishedHost;

            if (method == "OPTIONS")
            {
                string optionsHeaders =
                    "HTTP/1.1 204 No Content\r\n" +
                    $"Access-Control-Allow-Origin: {allowedOrigin}\r\n" +
                    "Access-Control-Allow-Methods: GET, HEAD, OPTIONS\r\n" +
                    "Access-Control-Allow-Headers: Range, Content-Type, Accept\r\n" +
                    "Access-Control-Max-Age: 86400\r\n";
                e.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 204, "No Content", optionsHeaders);
                return;
            }

            if (method != "GET" && method != "HEAD")
            {
                e.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 405, "Method Not Allowed", "Allow: GET, HEAD, OPTIONS\r\n");
                return;
            }

            string relativePath = uri.AbsolutePath;
            var (isAllowed, fullPath, reason) = MediaStreamService.ValidateAndResolvePath(_currentProjectRoot, relativePath);
            if (!isAllowed || fullPath == null)
            {
                _logger.LogWarning("Requisição ao host de mídia recusada ({Reason}): {Uri}", reason, e.Request.Uri);
                string forbiddenHeaders =
                    "HTTP/1.1 403 Forbidden\r\n" +
                    "Content-Type: text/plain; charset=utf-8\r\n" +
                    $"Access-Control-Allow-Origin: {allowedOrigin}\r\n";
                byte[] msgBytes = Encoding.UTF8.GetBytes($"Acesso negado: {reason}");
                e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(msgBytes), 403, "Forbidden", forbiddenHeaders);
                return;
            }

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning("Arquivo de mídia não encontrado: {Path}", fullPath);
                string notFoundHeaders =
                    "HTTP/1.1 404 Not Found\r\n" +
                    "Content-Type: text/plain; charset=utf-8\r\n" +
                    $"Access-Control-Allow-Origin: {allowedOrigin}\r\n";
                byte[] msgBytes = Encoding.UTF8.GetBytes("Arquivo não encontrado.");
                e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(msgBytes), 404, "Not Found", notFoundHeaders);
                return;
            }

            long fileLength = new FileInfo(fullPath).Length;
            string mime = PreviewPaths.MimeTypeFor(fullPath);
            string? rangeHeader = e.Request.Headers.Contains("Range") ? e.Request.Headers.GetHeader("Range") : null;

            var spec = MediaStreamService.BuildStreamSpec(rangeHeader, fileLength, mime, allowedOrigin);

            if (spec.StatusCode == 416)
            {
                _logger.LogWarning("HTTP 416 Range Not Satisfiable para {Uri}: Range={Range}, Length={Length}", e.Request.Uri, rangeHeader, fileLength);
                e.Response = core.Environment.CreateWebResourceResponse(Stream.Null, 416, spec.ReasonPhrase, spec.HeadersString);
                return;
            }

            if (method == "HEAD")
            {
                e.Response = core.Environment.CreateWebResourceResponse(Stream.Null, spec.StatusCode, spec.ReasonPhrase, spec.HeadersString);
                return;
            }

            var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 64 * 1024);

            Stream contentStream;
            if (spec.IsRange && spec.StatusCode == 206)
            {
                fs.Seek(spec.Start, SeekOrigin.Begin);
                contentStream = new BoundedReadStream(fs, spec.ContentLength);
            }
            else
            {
                contentStream = fs;
            }

            e.Response = core.Environment.CreateWebResourceResponse(contentStream, spec.StatusCode, spec.ReasonPhrase, spec.HeadersString);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro no servidor de streaming de mídia para {Uri}", e.Request.Uri);
        }
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
