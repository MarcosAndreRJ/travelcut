using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Ports;

namespace TravelCut.Infrastructure.Tooling;

public sealed class HttpModelDownloader : IModelDownloader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpModelDownloader> _logger;

    public HttpModelDownloader(HttpClient? httpClient = null, ILogger<HttpModelDownloader>? logger = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HttpModelDownloader>.Instance;
    }

    public async Task DownloadFileAsync(
        string url,
        string destinationFilePath,
        string expectedSha256,
        IProgress<DownloadProgressUpdate>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("A URL de download do modelo deve ser absoluta e utilizar obrigatoriamente o protocolo HTTPS.", nameof(url));
        }

        if (!Path.IsPathRooted(destinationFilePath) || destinationFilePath.Contains(".."))
        {
            throw new ArgumentException("O caminho de destino do download deve ser absoluto e sem sequências de travessia ('..').", nameof(destinationFilePath));
        }

        if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("O hash SHA-256 esperado deve conter exatamente 64 caracteres hexadecimais.", nameof(expectedSha256));
        }

        var destinationDir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        // 1. Verificar se o arquivo já existe e é válido com o SHA-256 esperado
        if (File.Exists(destinationFilePath))
        {
            try
            {
                var existingHash = await ComputeFileSha256Async(destinationFilePath, ct).ConfigureAwait(false);
                if (string.Equals(existingHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "Modelo já presente e íntegro em {Destination} com SHA-256 válido ({Hash}). Download dispensado.",
                        destinationFilePath, existingHash);

                    var len = new FileInfo(destinationFilePath).Length;
                    progress?.Report(new DownloadProgressUpdate(len, len, 100));
                    return;
                }

                _logger.LogWarning(
                    "Arquivo existente em {Destination} possui hash divergente ({Actual} vs {Expected}). Será baixado novamente e substituído com segurança.",
                    destinationFilePath, existingHash, expectedSha256);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Falha ao validar arquivo existente em {Destination}. Prosseguindo com download.", destinationFilePath);
            }
        }

        // 2. Iniciar requisição HTTP (apenas headers para checar tamanho e espaço em disco)
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;

        // 3. Checagem de espaço livre em disco
        CheckDiskSpace(destinationDir ?? ".", totalBytes);

        // 4. Download para arquivo temporário isolado
        var tempPath = destinationFilePath + ".tmp." + Guid.NewGuid().ToString("N");
        var completedSuccessfully = false;

        try
        {
            long bytesReadTotal = 0;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                    sha.TransformBlock(buffer, 0, bytesRead, null, 0);
                    bytesReadTotal += bytesRead;

                    double? pct = totalBytes.HasValue && totalBytes.Value > 0
                        ? Math.Round((double)bytesReadTotal / totalBytes.Value * 100, 1)
                        : null;

                    progress?.Report(new DownloadProgressUpdate(bytesReadTotal, totalBytes, pct));
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var hashBytes = sha.Hash ?? throw new InvalidOperationException("Falha ao calcular hash SHA-256.");
                var actualHash = Convert.ToHexStringLower(hashBytes);

                if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Hash SHA-256 do arquivo baixado é inválido. Esperado: '{expectedSha256}', obtido: '{actualHash}'. O download foi descartado.");
                }
            }

            // 5. Substituição atômica do arquivo destino
            File.Move(tempPath, destinationFilePath, overwrite: true);
            completedSuccessfully = true;

            _logger.LogInformation(
                "Download de modelo concluído com sucesso e verificado com SHA-256: {Destination} ({Bytes} bytes)",
                destinationFilePath, bytesReadTotal);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Download de modelo cancelado pelo usuário: {Url}", url);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha durante o download do modelo de {Url} para {Destination}", url, destinationFilePath);
            throw;
        }
        finally
        {
            if (!completedSuccessfully && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Não foi possível remover arquivo temporário de download {TempPath}", tempPath);
                }
            }
        }
    }

    private static void CheckDiskSpace(string targetDirectory, long? requiredBytes)
    {
        try
        {
            var fullPath = Path.GetFullPath(targetDirectory);
            var driveInfo = new DriveInfo(Path.GetPathRoot(fullPath) ?? fullPath);
            if (!driveInfo.IsReady) return;

            // Se o tamanho do arquivo é conhecido, exigir tamanho + margem de segurança de 300 MB;
            // Se desconhecido, exigir no mínimo 3.5 GB (tamanho aproximado do modelo large-v3).
            long minRequired = requiredBytes.HasValue && requiredBytes.Value > 0
                ? requiredBytes.Value + (300L * 1024 * 1024)
                : 3500L * 1024 * 1024;

            if (driveInfo.AvailableFreeSpace < minRequired)
            {
                long freeMb = driveInfo.AvailableFreeSpace / (1024 * 1024);
                long requiredMb = minRequired / (1024 * 1024);
                throw new InvalidOperationException(
                    $"Espaço em disco insuficiente na unidade '{driveInfo.Name}'. Disponível: {freeMb} MB, Necessário: {requiredMb} MB.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // Ignorar falhas na leitura da unidade (ex: caminhos UNC especiais em testes)
        }
    }

    public static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        using var sha = SHA256.Create();
        var hashBytes = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hashBytes);
    }
}
