namespace TravelCut.Application.Ipc;

/// <summary>
/// Política de origem do WebView2. Só o front publicado (https://travelcut.studio) pode enviar mensagens IPC
/// ou navegar; o dev server (http://localhost:5173) só é aceito quando <c>allowDevServer</c> for true
/// (o host passa true apenas em build DEBUG).
/// Pura (sem WPF): testável em xUnit.
/// </summary>
public static class IpcOriginPolicy
{
    public const string PublishedHost = "travelcut.studio";
    public const string DevServerOrigin = "http://localhost:5173";

    public static bool IsTrustedOrigin(string? url, bool allowDevServer)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        // Sem credenciais embutidas e na porta padrão (nada de https://travelcut.studio:8443).
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;

        if (uri.Scheme == Uri.UriSchemeHttps
            && string.Equals(uri.Host, PublishedHost, StringComparison.OrdinalIgnoreCase)
            && uri.IsDefaultPort)
        {
            return true;
        }

        return allowDevServer
            && uri.Scheme == Uri.UriSchemeHttp
            && string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            && uri.Port == 5173;
    }
}
