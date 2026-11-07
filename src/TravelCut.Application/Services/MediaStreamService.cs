using System.Globalization;
using TravelCut.Application.UseCases;

namespace TravelCut.Application.Services;

/// <summary>
/// Informações de resposta de streaming de mídia com suporte a HTTP 206 Partial Content.
/// </summary>
public sealed record MediaStreamSpec(
    int StatusCode,
    string ReasonPhrase,
    string ContentType,
    long ContentLength,
    long Start,
    long End,
    long TotalLength,
    bool IsRange,
    string HeadersString);

/// <summary>
/// Stream limitado para entregar exatamente a faixa de bytes solicitada pelo cabeçalho Range.
/// Fecha a stream subjacente adequadamente quando descartado.
/// </summary>
public sealed class BoundedReadStream : Stream
{
    private readonly Stream _inner;
    private long _remaining;

    public BoundedReadStream(Stream inner, long length)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _remaining = Math.Max(0, length);
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _remaining;
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_remaining <= 0)
            return 0;

        int toRead = (int)Math.Min(count, _remaining);
        int read = _inner.Read(buffer, offset, toRead);
        if (read <= 0)
            return 0;

        _remaining -= read;
        return read;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_remaining <= 0)
            return 0;

        int toRead = (int)Math.Min(count, _remaining);
        int read = await _inner.ReadAsync(buffer.AsMemory(offset, toRead), cancellationToken).ConfigureAwait(false);
        if (read <= 0)
            return 0;

        _remaining -= read;
        return read;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_remaining <= 0)
            return ValueTask.FromResult(0);

        int toRead = (int)Math.Min(buffer.Length, _remaining);
        var subBuffer = buffer[..toRead];
        var vt = _inner.ReadAsync(subBuffer, cancellationToken);
        if (vt.IsCompletedSuccessfully)
        {
            int read = vt.Result;
            _remaining -= read;
            return ValueTask.FromResult(read);
        }

        return new ValueTask<int>(AwaitedReadAsync(vt));
    }

    private async Task<int> AwaitedReadAsync(ValueTask<int> vt)
    {
        int read = await vt.ConfigureAwait(false);
        _remaining -= read;
        return read;
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Serviço puro de streaming de mídia (F13.6).
/// Implementa resolução restrita de arquivos, proteção contra path traversal,
/// validação de extensões autorizadas e cálculo de HTTP Range (206 Partial Content).
/// </summary>
public static class MediaStreamService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".mts", ".m2ts",
        ".m4a", ".mp3", ".wav", ".aac", ".flac", ".ogg",
        ".jpg", ".jpeg", ".png", ".webp"
    };

    /// <summary>
    /// Valida se o caminho requisitado é seguro e pertence estritamente a pastas autorizadas do projeto aberto.
    /// Bloqueia path traversal, symlinks que escapem da raiz e arquivos privados fora de 1-Bruto ou cache.
    /// </summary>
    public static (bool IsAllowed, string? FullPath, string Reason) ValidateAndResolvePath(string? projectRoot, string? relativeUriPath)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return (false, null, "Nenhum projeto aberto.");

        if (string.IsNullOrWhiteSpace(relativeUriPath))
            return (false, null, "Caminho não especificado.");

        string unescaped = Uri.UnescapeDataString(relativeUriPath).TrimStart('/', '\\').Replace('\\', '/');

        // Proteção contra caminhos absolutos no caminho relativo
        if (Path.IsPathRooted(unescaped) || unescaped.StartsWith("//", StringComparison.Ordinal) || unescaped.StartsWith(@"\\", StringComparison.Ordinal))
            return (false, null, "Caminho absoluto não permitido.");

        // Restrição estrita de pastas canônicas permitidas para servir mídia
        bool inBruto = unescaped.StartsWith("1-Bruto/", StringComparison.OrdinalIgnoreCase);
        bool inCache = unescaped.StartsWith("3-Trabalho/cache/", StringComparison.OrdinalIgnoreCase);
        bool inTrabalho = unescaped.StartsWith("2-Trabalho/", StringComparison.OrdinalIgnoreCase);

        if (!inBruto && !inCache && !inTrabalho)
            return (false, null, "Acesso restrito exclusivamente a pastas de mídia autorizadas (1-Bruto ou cache).");

        // Extensão de arquivo permitida
        string ext = Path.GetExtension(unescaped).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return (false, null, $"Extensão '{ext}' não autorizada para streaming de mídia.");

        // Resolução segura de path traversal
        if (!PreviewPaths.TryResolveInsideRoot(projectRoot, unescaped, out string fullPath))
            return (false, null, "Caminho fora dos limites da raiz do projeto (path traversal detectado).");

        // Checagem de symlinks para evitar apontamento externo
        try
        {
            var info = new FileInfo(fullPath);
            if (info.Exists && info.LinkTarget != null)
            {
                string targetFull = Path.GetFullPath(Path.Combine(info.DirectoryName ?? projectRoot, info.LinkTarget));
                string rootCanonical = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!targetFull.StartsWith(rootCanonical, StringComparison.OrdinalIgnoreCase))
                {
                    return (false, null, "Symlink aponta para fora da raiz autorizada do projeto.");
                }
            }
        }
        catch
        {
            // Falha ao inspecionar atributos do arquivo
        }

        return (true, fullPath, null);
    }

    /// <summary>
    /// Interpreta o cabeçalho HTTP Range (ex: "bytes=0-1048575", "bytes=1000-", "bytes=-500").
    /// </summary>
    public static (bool IsRange, long Start, long End, bool IsSatisfiable) ParseRange(string? rangeHeader, long fileLength)
    {
        if (string.IsNullOrWhiteSpace(rangeHeader) || fileLength <= 0)
            return (false, 0, Math.Max(0, fileLength - 1), true);

        string trimmed = rangeHeader.Trim();
        if (!trimmed.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            return (false, 0, fileLength - 1, true);

        string spec = trimmed["bytes=".Length..].Trim();
        int dashIdx = spec.IndexOf('-');
        if (dashIdx < 0)
            return (false, 0, fileLength - 1, true);

        string startPart = spec[..dashIdx].Trim();
        string endPart = spec[(dashIdx + 1)..].Trim();

        long start = 0;
        long end = fileLength - 1;

        if (string.IsNullOrEmpty(startPart))
        {
            // Formato sufixo: bytes=-500 (últimos 500 bytes)
            if (long.TryParse(endPart, NumberStyles.None, CultureInfo.InvariantCulture, out long suffixLength))
            {
                if (suffixLength <= 0)
                    return (true, 0, 0, false);

                start = Math.Max(0, fileLength - suffixLength);
                end = fileLength - 1;
            }
            else
            {
                return (false, 0, fileLength - 1, true);
            }
        }
        else
        {
            if (!long.TryParse(startPart, NumberStyles.None, CultureInfo.InvariantCulture, out start))
                return (false, 0, fileLength - 1, true);

            if (start >= fileLength || start < 0)
                return (true, start, end, false);

            if (!string.IsNullOrEmpty(endPart))
            {
                if (long.TryParse(endPart, NumberStyles.None, CultureInfo.InvariantCulture, out long parsedEnd))
                {
                    end = Math.Min(parsedEnd, fileLength - 1);
                }
            }
        }

        if (start > end)
            return (true, start, end, false);

        return (true, start, end, true);
    }

    /// <summary>
    /// Constrói os cabeçalhos de resposta HTTP para streaming de mídia (200 ou 206).
    /// </summary>
    public static MediaStreamSpec BuildStreamSpec(
        string? rangeHeader,
        long fileLength,
        string mimeType,
        string allowedOrigin)
    {
        var (isRange, start, end, satisfiable) = ParseRange(rangeHeader, fileLength);

        if (isRange && !satisfiable)
        {
            string failHeaders =
                $"HTTP/1.1 416 Range Not Satisfiable\r\n" +
                $"Content-Range: bytes */{fileLength}\r\n" +
                $"Accept-Ranges: bytes\r\n" +
                $"Access-Control-Allow-Origin: {allowedOrigin}\r\n" +
                $"Access-Control-Allow-Methods: GET, HEAD, OPTIONS\r\n" +
                $"Access-Control-Allow-Headers: Range\r\n" +
                $"Access-Control-Expose-Headers: Content-Range, Content-Length, Accept-Ranges\r\n";

            return new MediaStreamSpec(416, "Range Not Satisfiable", mimeType, 0, 0, 0, fileLength, true, failHeaders);
        }

        if (isRange)
        {
            long contentLength = (end - start) + 1;
            string headers =
                $"HTTP/1.1 206 Partial Content\r\n" +
                $"Content-Type: {mimeType}\r\n" +
                $"Content-Range: bytes {start}-{end}/{fileLength}\r\n" +
                $"Content-Length: {contentLength}\r\n" +
                $"Accept-Ranges: bytes\r\n" +
                $"Access-Control-Allow-Origin: {allowedOrigin}\r\n" +
                $"Access-Control-Allow-Methods: GET, HEAD, OPTIONS\r\n" +
                $"Access-Control-Allow-Headers: Range\r\n" +
                $"Access-Control-Expose-Headers: Content-Range, Content-Length, Accept-Ranges\r\n";

            return new MediaStreamSpec(206, "Partial Content", mimeType, contentLength, start, end, fileLength, true, headers);
        }
        else
        {
            string headers =
                $"HTTP/1.1 200 OK\r\n" +
                $"Content-Type: {mimeType}\r\n" +
                $"Content-Length: {fileLength}\r\n" +
                $"Accept-Ranges: bytes\r\n" +
                $"Access-Control-Allow-Origin: {allowedOrigin}\r\n" +
                $"Access-Control-Allow-Methods: GET, HEAD, OPTIONS\r\n" +
                $"Access-Control-Allow-Headers: Range\r\n" +
                $"Access-Control-Expose-Headers: Content-Range, Content-Length, Accept-Ranges\r\n";

            return new MediaStreamSpec(200, "OK", mimeType, fileLength, 0, Math.Max(0, fileLength - 1), fileLength, false, headers);
        }
    }
}
