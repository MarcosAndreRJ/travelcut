using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Ipc;

public record IpcRequest
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public object? Payload { get; init; }
}

public record IpcResponse
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = string.Empty;

    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("success")]
    public bool IsSuccess => Ok;

    [JsonPropertyName("payload")]
    public object? Payload { get; init; }

    [JsonPropertyName("data")]
    public object? Data => Payload;

    [JsonPropertyName("error")]
    public IpcError? Error { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode => Error?.Code;

    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage => Error?.Message;

    [JsonPropertyName("errorDetails")]
    public string? ErrorDetails => Error?.Details;

    public static IpcResponse Success(string requestId, object? payload = null) => new()
    {
        RequestId = requestId,
        Ok = true,
        Payload = payload
    };

    public static IpcResponse Fail(string requestId, string code, string message, string? details = null) => new()
    {
        RequestId = requestId,
        Ok = false,
        Error = new IpcError(code, message, details)
    };
}

public record IpcEvent
{
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public object? Payload { get; init; }

    public static IpcEvent Create(string type, object? payload) => new()
    {
        Type = type,
        Payload = payload
    };
}

public record IpcError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("details")] string? Details = null
);
