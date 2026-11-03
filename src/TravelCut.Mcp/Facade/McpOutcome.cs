using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TravelCut.Contracts.Ipc;

namespace TravelCut.Mcp.Facade;

/// <summary>Códigos de erro próprios do servidor MCP. Os códigos do dispatcher são repassados como estão.</summary>
public static class McpErrorCodes
{
    public const string MutationsDisabled = "MUTATIONS_DISABLED";
    public const string ProjectNotAuthorized = "PROJECT_NOT_AUTHORIZED";
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string IdempotencyKeyReused = "IDEMPOTENCY_KEY_REUSED";
    public const string ConfirmationRequired = "CONFIRMATION_REQUIRED";
    public const string SuggestionNotFound = "SUGGESTION_NOT_FOUND";
    public const string MediaNotFound = "MEDIA_NOT_FOUND";
    public const string JobNotFound = "JOB_NOT_FOUND";
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>Falhas transitórias: não ficam no cache de idempotência, para que a mesma chave possa ser repetida.</summary>
    public static bool IsTransient(string? code) => code is
        InternalError or "TIMEOUT" or "EDITOR_UNAVAILABLE" or "PIPELINE_IN_PROGRESS" or "OPERATION_IN_PROGRESS" or "DISK_FULL";
}

/// <summary>Erro previsível da fachada: vira resultado estruturado com isError, nunca exceção crua ao cliente.</summary>
public sealed class McpFailure(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Resultado de uma ferramenta: JSON de sucesso ou {code, message} com isError.</summary>
public sealed record McpOutcome(bool IsError, string Text, string? ErrorCode)
{
    /// <summary>
    /// Resultado que NÃO entra no cache de idempotência: a mesma chave pode executar de novo. Usado quando o IPC
    /// devolve sucesso técnico com status de falha (ex.: apply_plan com status ERROR/UNAVAILABLE). Padrão: cacheável.
    /// </summary>
    public bool NoCache { get; init; }

    public static McpOutcome Success(string json) => new(false, json, null);

    /// <summary>Mesmo resultado, marcado como não cacheável.</summary>
    public McpOutcome AsNotCacheable() => this with { NoCache = true };

    public static McpOutcome Error(string code, string message) =>
        new(true, JsonSerializer.Serialize(new McpErrorBody(code, Sanitize(message))), code);

    /// <summary>
    /// Converte a resposta do dispatcher. <paramref name="shape"/> adapta o payload (filtro, projeção) antes de
    /// serializar; a redação de caminhos absolutos é aplicada depois, em qualquer caso.
    /// </summary>
    public static McpOutcome FromResponse(IpcResponse response, Func<object?, object?>? shape = null)
    {
        ArgumentNullException.ThrowIfNull(response);

        if (!response.Ok)
        {
            return Error(response.Error?.Code ?? McpErrorCodes.InternalError, response.Error?.Message ?? "Falha na operação.");
        }

        object? payload = shape is null ? response.Payload : shape(response.Payload);
        var node = JsonSerializer.SerializeToNode(payload);
        return Success(RedactAndSerialize(node));
    }

    /// <summary>Remove chaves que carregam caminhos absolutos. Mantém o restante do JSON do contrato.</summary>
    public static string RedactAndSerialize(JsonNode? node)
    {
        if (node is null)
        {
            return "null";
        }
        RemoveSensitiveKeys(node);
        return node.ToJsonString();
    }

    private static void RemoveSensitiveKeys(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    if (SensitiveKeys.Contains(key))
                    {
                        obj.Remove(key);
                    }
                }
                foreach (var child in obj.Select(kv => kv.Value).OfType<JsonNode>())
                {
                    RemoveSensitiveKeys(child);
                }
                break;
            case JsonArray arr:
                foreach (var child in arr.OfType<JsonNode>())
                {
                    RemoveSensitiveKeys(child);
                }
                break;
        }
    }

    /// <summary>Propriedades de caminho absoluto dos DTOs. O agente trabalha com IDs e caminhos relativos.</summary>
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "rootPath", "fullPath", "absolutePath", "outputAbsolutePath",
    };

    // Caminhos absolutos em mensagens de erro: Windows (X:\ e X:/, letra isolada: não casa "https:/"), UNC (\\servidor)
    // e POSIX (/a/b, sem casar 1-Bruto/x.mp4 nem URLs). O caminho POSIX exige outra barra depois do início.
    private static readonly Regex AbsolutePathPattern = new(
        @"(?:(?<!\w)[A-Za-z]:[\\/]|\\\\|(?<![\w.~:/])/(?=[^\s""'<>|]*/))[^\s""'<>|]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private const int MaxMessageLength = 500;

    public static string Sanitize(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }
        var cleaned = AbsolutePathPattern.Replace(message, "[caminho]");
        return cleaned.Length <= MaxMessageLength ? cleaned : cleaned[..MaxMessageLength] + "…";
    }
}

public sealed record McpErrorBody(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
