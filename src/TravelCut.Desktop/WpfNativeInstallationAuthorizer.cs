using System.Text;
using System.Windows;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;

namespace TravelCut.Desktop;

public sealed class WpfNativeInstallationAuthorizer : INativeInstallationAuthorizer
{
    private readonly ILogger<WpfNativeInstallationAuthorizer> _logger;

    public WpfNativeInstallationAuthorizer(ILogger<WpfNativeInstallationAuthorizer> logger)
    {
        _logger = logger;
    }

    public Task<bool> RequestInstallationAuthorizationAsync(
        NativeInstallationRequest request,
        CancellationToken ct = default)
    {
        var app = System.Windows.Application.Current;
        if (app == null)
        {
            _logger.LogWarning("Aplicação WPF não disponível para confirmação nativa. Rejeitando por padrão.");
            return Task.FromResult(false);
        }

        return app.Dispatcher.InvokeAsync(() =>
        {
            var window = app.MainWindow;
            var sb = new StringBuilder();
            sb.AppendLine("TRAVELCUT STUDIO — AUTORIZAÇÃO DE INSTALAÇÃO NO SISTEMA");
            sb.AppendLine();
            sb.AppendLine("Deseja autorizar a instalação da seguinte ferramenta?");
            sb.AppendLine($" • Ferramenta: {request.Title} ({request.ToolKey})");
            sb.AppendLine($" • Descrição: {request.Description}");
            if (!string.IsNullOrWhiteSpace(request.TargetDirectory))
            {
                sb.AppendLine($" • Diretório de Destino: {request.TargetDirectory}");
            }
            sb.AppendLine();
            sb.AppendLine("Ações que serão executadas no seu computador:");
            foreach (var step in request.Steps)
            {
                sb.AppendLine($"   [Passo] {step}");
            }
            if (request.RequiresElevation)
            {
                sb.AppendLine();
                sb.AppendLine("AVISO: Esta instalação pode solicitar privilégios de Administrador (UAC).");
            }
            sb.AppendLine();
            sb.AppendLine("Esta é uma confirmação nativa de segurança. Scripts do WebView não têm controle sobre esta decisão.");
            sb.AppendLine("Clique em 'Sim' para AUTORIZAR ou 'Não' para CANCELAR.");

            var result = MessageBox.Show(
                window,
                sb.ToString(),
                $"Autorização Nativa: {request.Title}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            var authorized = result == MessageBoxResult.Yes;
            if (authorized)
            {
                _logger.LogInformation("Instalação de '{ToolKey}' AUTORIZADA pelo usuário no diálogo nativo do host.", request.ToolKey);
            }
            else
            {
                _logger.LogWarning("Instalação de '{ToolKey}' REJEITADA/CANCELADA pelo usuário no diálogo nativo do host.", request.ToolKey);
            }

            return authorized;
        }).Task;
    }

    public Task<bool> RequestDownloadAuthorizationAsync(
        NativeDownloadRequest request,
        CancellationToken ct = default)
    {
        var app = System.Windows.Application.Current;
        if (app == null)
        {
            _logger.LogWarning("Aplicação WPF não disponível para confirmação nativa. Rejeitando por padrão.");
            return Task.FromResult(false);
        }

        return app.Dispatcher.InvokeAsync(() =>
        {
            var window = app.MainWindow;
            var sb = new StringBuilder();
            sb.AppendLine("TRAVELCUT STUDIO — AUTORIZAÇÃO DE DOWNLOAD DE MODELO");
            sb.AppendLine();
            sb.AppendLine("Deseja autorizar o download do modelo de inteligência artificial?");
            sb.AppendLine($" • Modelo: {request.ModelName}");
            sb.AppendLine($" • Tamanho aproximado: {request.DownloadSize}");
            sb.AppendLine($" • Diretório no disco: {request.DestinationDirectory}");
            sb.AppendLine();
            sb.AppendLine("Esta é uma confirmação nativa de segurança. O download consumirá espaço e rede.");
            sb.AppendLine("Clique em 'Sim' para AUTORIZAR ou 'Não' para CANCELAR.");

            var result = MessageBox.Show(
                window,
                sb.ToString(),
                $"Autorização de Download: {request.ModelName}",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            var authorized = result == MessageBoxResult.Yes;
            if (authorized)
            {
                _logger.LogInformation("Download do modelo '{ModelName}' AUTORIZADO pelo usuário no diálogo nativo do host.", request.ModelName);
            }
            else
            {
                _logger.LogWarning("Download do modelo '{ModelName}' REJEITADO/CANCELADA pelo usuário no diálogo nativo do host.", request.ModelName);
            }

            return authorized;
        }).Task;
    }
}
