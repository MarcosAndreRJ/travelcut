using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace TravelCut.Infrastructure.Logging;

/// <summary>
/// Log em arquivo do Desktop: <c>%LOCALAPPDATA%\TravelCutStudio\logs\travelcut-yyyyMMdd.log</c>.
/// - Rotação diária (um arquivo por data local); mantém os <see cref="RetainedFiles"/> mais recentes.
/// - Escrita em thread de fundo por fila limitada: chamar Log nunca bloqueia a UI (mensagens excedentes são descartadas).
/// - Cada arquivo começa com a versão do app e o sistema operacional.
/// - Mascara o perfil do usuário (caminho real vira "%USERPROFILE%") e segredos (token, senha, Bearer) antes de gravar.
/// O servidor MCP não usa este provider: continua logando somente no stderr.
/// </summary>
public sealed class TravelCutFileLoggerProvider : ILoggerProvider
{
    public const int RetainedFiles = 14;
    public const string FilePrefix = "travelcut-";
    public const string FileExtension = ".log";
    public const int QueueCapacity = 10_000;

    public static string SessionId { get; } = Guid.NewGuid().ToString("N")[..8];
    public static string DefaultLogsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TravelCutStudio", "logs");

    private readonly string _directory;
    private readonly LogLevel _minimumLevel;
    private readonly string _header;
    private readonly string? _userProfile;
    private readonly TimeProvider _time;
    private readonly Channel<LogLine> _queue;
    private readonly Task _writer;
    private readonly object _disposeLock = new();
    private bool _disposed;

    /// <param name="directory">Pasta de logs (criada sob demanda).</param>
    /// <param name="minimumLevel">Nível mínimo gravado (Information em Release, Debug em DEBUG).</param>
    /// <param name="header">Primeira linha de cada arquivo: versão do app e SO (ver <see cref="BuildHeader"/>).</param>
    /// <param name="userProfile">Perfil a mascarar. Nulo: pasta do perfil do usuário atual.</param>
    /// <param name="timeProvider">Relógio (testes). Nulo: relógio do sistema.</param>
    public TravelCutFileLoggerProvider(
        string directory,
        LogLevel minimumLevel,
        string header,
        string? userProfile = null,
        TimeProvider? timeProvider = null)
    {
        _directory = directory;
        _minimumLevel = minimumLevel;
        _header = header;
        _userProfile = string.IsNullOrWhiteSpace(userProfile)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : userProfile;
        _time = timeProvider ?? TimeProvider.System;

        _queue = Channel.CreateBounded<LogLine>(new BoundedChannelOptions(QueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite,
        });

        // Poda inicial: o limite vale também para arquivos de execuções anteriores.
        TryPrune();
        // Laço assíncrono: não bloqueia o chamador; roda até a fila ser concluída (Dispose).
        _writer = WriteLoop();
    }

    /// <summary>Pasta padrão: %LOCALAPPDATA%\TravelCutStudio\logs.</summary>
    public static string DefaultDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TravelCutStudio", "logs");

    /// <summary>Primeira linha do arquivo: versão do app, SO e runtime.</summary>
    public static string BuildHeader(string appVersion)
        => $"=== TravelCut Studio {appVersion} | SO: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}) | .NET {Environment.Version} ===";

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    internal void Enqueue(LogLine line)
    {
        if (_disposed) return;
        // TryWrite nunca espera: a UI não fica bloqueada mesmo com a fila cheia.
        _queue.Writer.TryWrite(line);
    }

    internal bool IsEnabledFor(LogLevel level)
        => level != LogLevel.None && level >= _minimumLevel;

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _queue.Writer.TryComplete();
        try
        {
            // Espera curta: o encerramento drena a fila, mas nunca trava o fechamento do app.
            _writer.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // Erros de escrita já são tratados no laço.
        }
    }

    private async Task WriteLoop()
    {
        var state = new WriterState();
        try
        {
            await foreach (var line in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    WriteLine(state, line);
                }
                catch (Exception)
                {
                    // Falha de disco/permissão no log nunca derruba o app.
                }
            }
        }
        finally
        {
            state.Dispose();
        }
    }

    private void WriteLine(WriterState state, LogLine line)
    {
        string day = line.Timestamp.ToLocalTime().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        if (state.Writer == null || state.Day != day)
        {
            state.Writer?.Dispose();
            state.Writer = null;

            Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, FilePrefix + day + FileExtension);
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            state.Writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
            state.Day = day;

            state.Writer.WriteLine(LogRedactor.Redact(_header, _userProfile));
            TryPrune();
        }

        state.Writer.WriteLine(LogRedactor.Redact(FormatLine(line), _userProfile));
    }

    private string FormatLine(LogLine line)
    {
        var sb = new StringBuilder(256);
        sb.Append(line.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        sb.Append(" [").Append(SessionId).Append("] ");
        sb.Append("[").Append(LevelName(line.Level)).Append("] ");
        sb.Append(line.Category).Append(": ");
        sb.Append(line.Message);
        if (!string.IsNullOrEmpty(line.Exception))
        {
            sb.Append(Environment.NewLine).Append(line.Exception);
        }
        return sb.ToString();
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    /// <summary>Remove os arquivos diários além dos <see cref="RetainedFiles"/> mais recentes. Melhor esforço.</summary>
    private void TryPrune()
    {
        try
        {
            if (!Directory.Exists(_directory)) return;

            var names = Directory.EnumerateFiles(_directory, FilePrefix + "*" + FileExtension)
                .Select(Path.GetFileName)
                .Where(n => n != null && IsDailyFileName(n))
                .Select(n => n!)
                .OrderByDescending(n => n, StringComparer.Ordinal)
                .ToList();

            foreach (var name in names.Skip(RetainedFiles))
            {
                try
                {
                    File.Delete(Path.Combine(_directory, name));
                }
                catch (IOException)
                {
                    // Em uso por outro processo: fica para a próxima poda.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem pasta ou sem permissão: nada a podar.
        }
    }

    /// <summary>Nome exato "travelcut-" + 8 dígitos + ".log" (ignora outros arquivos da pasta).</summary>
    public static bool IsDailyFileName(string name)
    {
        if (name.Length != FilePrefix.Length + 8 + FileExtension.Length) return false;
        if (!name.StartsWith(FilePrefix, StringComparison.Ordinal) || !name.EndsWith(FileExtension, StringComparison.Ordinal)) return false;
        for (int i = FilePrefix.Length; i < FilePrefix.Length + 8; i++)
        {
            if (!char.IsAsciiDigit(name[i])) return false;
        }
        return true;
    }

    internal sealed record LogLine(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, string? Exception);

    private sealed class WriterState : IDisposable
    {
        public StreamWriter? Writer { get; set; }
        public string? Day { get; set; }

        public void Dispose() => Writer?.Dispose();
    }

    private sealed class FileLogger : ILogger
    {
        private readonly TravelCutFileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(TravelCutFileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabledFor(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            string message;
            try
            {
                message = formatter(state, exception);
            }
            catch (Exception)
            {
                message = "(mensagem de log não formatável)";
            }

            _provider.Enqueue(new LogLine(
                _provider._time.GetUtcNow(),
                logLevel,
                _category,
                message,
                exception?.ToString()));
        }
    }
}

/// <summary>
/// Mascaramento antes de gravar: perfil do usuário vira "%USERPROFILE%" (barras \ e / ) e valores de segredos
/// (token, senha, chave de API, Bearer, Authorization) viram "[REDACTADO]".
/// </summary>
public static class LogRedactor
{
    public const string Redacted = "[REDACTADO]";

    private static readonly System.Text.RegularExpressions.Regex BearerPattern = new(
        @"Bearer\s+[A-Za-z0-9\-._~+/]+=*",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex SecretPattern = new(
        @"(?<key>\b(?:access[_-]?token|refresh[_-]?token|client[_-]?secret|api[_-]?key|token|secret|password|senha|authorization|cookie)\b""?\s*[:=]\s*)""?[^\s,;""'}]+""?",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static string Redact(string text, string? userProfile)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            text = ReplaceIgnoreCase(text, userProfile, UserProfileTokenValue);
            string forwardSlash = userProfile.Replace('\\', '/');
            if (!string.Equals(forwardSlash, userProfile, StringComparison.Ordinal))
            {
                text = ReplaceIgnoreCase(text, forwardSlash, UserProfileTokenValue);
            }
        }

        text = BearerPattern.Replace(text, "Bearer " + Redacted);
        text = SecretPattern.Replace(text, m => m.Groups["key"].Value + Redacted);
        return text;
    }

    private const string UserProfileTokenValue = "%USERPROFILE%";

    private static string ReplaceIgnoreCase(string text, string value, string replacement)
        => System.Text.RegularExpressions.Regex.Replace(
            text,
            System.Text.RegularExpressions.Regex.Escape(value),
            replacement.Replace("$", "$$"),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}
