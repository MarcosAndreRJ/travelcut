using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TravelCut.Application.Editor;
using TravelCut.Application.Ipc;
using TravelCut.Contracts.Dtos;
using TravelCut.Contracts.Ipc;
using TravelCut.Mcp.Configuration;

namespace TravelCut.Mcp.Facade;

/// <summary>
/// Fachada MCP sobre o IpcDispatcher do Application Core: cada ferramenta vira uma requisição IPC que a UI também usa.
/// Não há regra de negócio aqui. Responsabilidades desta classe: guardas (modo de mutação, projeto autorizado,
/// chave de idempotência, confirmação), escopo de DI por chamada, mapeamento de erros e logs sem conteúdo sensível.
/// </summary>
public sealed partial class TravelCutMcpFacade
{
    /// <summary>Tipos IPC usados pelo servidor (nomes reais do IpcDispatcher).</summary>
    public static class IpcTypes
    {
        public const string ProjectRecent = "project.recent";
        public const string MediaList = "media.list";
        public const string AnalysisGetMedia = "analysis.getMedia";
        public const string SuggestionsList = "suggestions.list";
        public const string PlanGet = "plan.get";
        public const string PipelineStatus = "pipeline.status";
        public const string PipelineStart = "pipeline.start";
        public const string DecisionSubmit = "decision.submit";
        public const string EditorCapabilities = EditorIpcHandler.CapabilitiesType;
        public const string MotionCapabilities = TravelCut.Application.Motion.MotionIpcHandler.CapabilitiesType;
        public const string EditorPrepareApply = EditorIpcHandler.PrepareApplyType;
        public const string EditorApply = EditorIpcHandler.ApplyType;
    }

    public const int MaxMediaIdsPerAnalysis = 200;
    public const int MaxReviewerLength = 100;
    public const int MaxNotesLength = 2000;

    private static readonly string[] SuggestionStatuses = ["Proposed", "Approved", "Rejected", "Deferred"];
    private static readonly string[] ReviewActions = ["approve", "reject", "defer", "modify"];

    private readonly IServiceScopeFactory _scopes;
    private readonly McpIdempotencyCache _cache;
    private readonly ILogger<TravelCutMcpFacade> _logger;

    public TravelCutMcpFacade(
        IServiceScopeFactory scopes,
        McpServerConfiguration configuration,
        McpIdempotencyCache cache,
        ILogger<TravelCutMcpFacade> logger)
    {
        _scopes = scopes;
        Configuration = configuration;
        _cache = cache;
        _logger = logger;
    }

    public McpServerConfiguration Configuration { get; }

    public bool IsAuthorized(Guid projectId) => Configuration.AuthorizedProjects.Contains(projectId);

    // ---- Validações reutilizáveis ----

    /// <summary>GUID válido + projeto autorizado. Ordem: formato (INVALID_ARGUMENT) e depois autorização.</summary>
    public Guid RequireAuthorizedProject(string? raw)
    {
        var id = ParseGuid(raw, "projectId");
        if (!IsAuthorized(id))
        {
            throw new McpFailure(McpErrorCodes.ProjectNotAuthorized, "Projeto não autorizado para este servidor MCP.");
        }
        return id;
    }

    public static Guid ParseGuid(string? raw, string field)
    {
        if (string.IsNullOrWhiteSpace(raw) || !Guid.TryParse(raw, out var id) || id == Guid.Empty)
        {
            throw new McpFailure(McpErrorCodes.InvalidArgument, $"{field} deve ser um GUID válido.");
        }
        return id;
    }

    /// <summary>Chave obrigatória no formato [A-Za-z0-9_-]{1,100}. \z evita aceitar quebra de linha final.</summary>
    public static string RequireIdempotencyKey(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || !IdempotencyKeyPattern().IsMatch(raw))
        {
            throw new McpFailure(McpErrorCodes.InvalidArgument,
                "idempotencyKey é obrigatória e deve ter de 1 a 100 caracteres [A-Za-z0-9_-].");
        }
        return raw;
    }

    /// <summary>Filtro opcional de status efetivo (null = sem filtro). Valor inválido → INVALID_ARGUMENT.</summary>
    public static string? RequireStatusFilter(string? raw)
    {
        if (raw is null) return null;
        var match = SuggestionStatuses.FirstOrDefault(s => string.Equals(s, raw, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new McpFailure(McpErrorCodes.InvalidArgument,
            "status inválido. Use Proposed, Approved, Rejected ou Deferred.");
    }

    public static string RequireReviewAction(string? raw)
    {
        var match = ReviewActions.FirstOrDefault(a => string.Equals(a, raw, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new McpFailure(McpErrorCodes.InvalidArgument,
            "action inválida. Use approve, reject, defer ou modify.");
    }

    /// <summary>Forma canônica de um GUID de entrada para o hash de argumentos (aceita maiúsculas/minúsculas).</summary>
    public static string Canonical(string? raw) =>
        Guid.TryParse(raw, out var id) ? id.ToString("D") : raw ?? string.Empty;

    // ---- Dispatcher ----

    /// <summary>Executa a requisição no mesmo IpcDispatcher da UI, em escopo próprio (DbContext por chamada).</summary>
    public async Task<IpcResponse> DispatchAsync(string type, object? payload, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IpcDispatcher>();
        return await dispatcher.DispatchAsync(new IpcRequest { Type = type, Payload = payload }, ct);
    }

    // ---- Execução de ferramentas ----

    /// <summary>Leitura: converte exceções em erro estruturado. Cancelamento do cliente é propagado.</summary>
    public async Task<McpOutcome> ExecuteReadAsync(string tool, Func<CancellationToken, Task<McpOutcome>> body, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(tool);
        ArgumentNullException.ThrowIfNull(body);
        return await RunBodyAsync(tool, body, ct);
    }

    /// <summary>
    /// Argumentos validados de uma mutação: <paramref name="Value"/> alimenta o corpo; <paramref name="CanonicalArgs"/>
    /// (com os valores já validados e normalizados) define o hash de idempotência.
    /// </summary>
    public sealed record ValidatedMutation<T>(T Value, object CanonicalArgs);

    /// <summary>
    /// Mutação guardada, nesta ordem: mutações habilitadas → projeto (formato e autorização) → chave de idempotência →
    /// <paramref name="validate"/> (entradas do corpo; McpFailure não entra no cache, então a mesma chave pode ser
    /// reenviada corrigida) → cache (mesmo resultado sem re-executar / IDEMPOTENCY_KEY_REUSED) → corpo (IPC).
    /// O corpo roda com o token do servidor; <paramref name="ct"/> é só do chamador (cancelar não cancela a mutação).
    /// </summary>
    public async Task<McpOutcome> ExecuteMutationAsync<T>(
        string tool,
        string? projectIdRaw,
        string? idempotencyKeyRaw,
        Func<ValidatedMutation<T>> validate,
        Func<Guid, T, CancellationToken, Task<McpOutcome>> body,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(tool);
        ArgumentNullException.ThrowIfNull(validate);
        ArgumentNullException.ThrowIfNull(body);

        if (!Configuration.AllowMutations)
        {
            LogMutationBlocked(tool);
            return McpOutcome.Error(McpErrorCodes.MutationsDisabled,
                "Mutações desligadas neste servidor. Inicie com --allow-mutations para habilitar.");
        }

        Guid projectId;
        string key;
        ValidatedMutation<T> validated;
        try
        {
            projectId = RequireAuthorizedProject(projectIdRaw);
            key = RequireIdempotencyKey(idempotencyKeyRaw);
            validated = validate();
        }
        catch (McpFailure failure)
        {
            LogMutationRejected(tool, failure.Code);
            return McpOutcome.Error(failure.Code, failure.Message);
        }

        string argumentsHash = HashArguments(validated.CanonicalArgs);
        LogMutationIntent(tool, projectId, key);

        var result = await _cache.RunAsync(tool, key, argumentsHash,
            serverCt => RunBodyAsync(tool, c => body(projectId, validated.Value, c), serverCt), ct);

        LogMutationResult(tool, projectId, key, result);
        return result.Outcome;
    }

    /// <summary>
    /// Confere, pelo mesmo IPC da UI (media.list), que todas as mídias pertencem ao projeto. Falha = McpFailure
    /// (MEDIA_NOT_FOUND, sem revelar se a mídia existe em outro projeto).
    /// </summary>
    public async Task RequireMediaInProjectAsync(Guid projectId, IEnumerable<Guid> mediaIds, CancellationToken ct)
    {
        var response = await DispatchAsync(IpcTypes.MediaList, new ProjectRequest(projectId), ct);
        if (!response.Ok)
        {
            throw new McpFailure(response.Error?.Code ?? McpErrorCodes.InternalError,
                response.Error?.Message ?? "Falha ao listar mídias do projeto.");
        }

        var known = IdsOf(response.Payload, "media", "mediaId");
        if (mediaIds.Any(id => !known.Contains(id)))
        {
            throw new McpFailure(McpErrorCodes.MediaNotFound, "Mídia não encontrada neste projeto.");
        }
    }

    /// <summary>GUIDs de um array do payload (ex.: direto array ou "media" → "mediaId"). Itens sem GUID válido são ignorados.</summary>
    public static HashSet<Guid> IdsOf(object? payload, string arrayName, string idName)
    {
        var ids = new HashSet<Guid>();
        var node = JsonSerializer.SerializeToNode(payload);
        JsonArray? items = null;

        if (node is JsonArray directArray)
        {
            items = directArray;
        }
        else if (node is JsonObject obj && obj[arrayName] is JsonArray wrappedArray)
        {
            items = wrappedArray;
        }

        if (items is null)
        {
            return ids;
        }

        foreach (var item in items)
        {
            if (item?[idName]?.GetValue<string>() is { } raw && Guid.TryParse(raw, out var id))
            {
                ids.Add(id);
            }
        }
        return ids;
    }

    private async Task<McpOutcome> RunBodyAsync(string tool, Func<CancellationToken, Task<McpOutcome>> body, CancellationToken ct)
    {
        try
        {
            return await body(ct);
        }
        catch (McpFailure failure)
        {
            return McpOutcome.Error(failure.Code, failure.Message);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Detalhes ficam só no log (tipo + mensagem sanitizada, sem stack); o cliente recebe código genérico.
            LogToolFailure(ex, tool);
            return McpOutcome.Error(McpErrorCodes.InternalError, "Erro interno do servidor MCP. Veja o log (stderr).");
        }
    }

    private static string HashArguments(object canonicalArgs)
    {
        var json = JsonSerializer.Serialize(canonicalArgs);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    // ---- Logs (sem caminhos, conteúdo de transcrição, notas ou nomes de revisor) ----

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP mutação {Tool} bloqueada: mutações desligadas")]
    private partial void LogMutationBlocked(string tool);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP mutação {Tool} recusada antes da execução: código {ErrorCode}")]
    private partial void LogMutationRejected(string tool, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP mutação {Tool} solicitada: projeto {ProjectId}, chave {IdempotencyKey}")]
    private partial void LogMutationIntent(string tool, Guid projectId, string idempotencyKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP mutação {Tool} concluída: projeto {ProjectId}, chave {IdempotencyKey}, resultado {Result}, código {ErrorCode}, reaproveitado {Reused}")]
    private partial void LogMutationOutcome(string tool, Guid projectId, string idempotencyKey, string result, string? errorCode, bool reused);

    [LoggerMessage(Level = LogLevel.Error, Message = "Erro interno na ferramenta MCP {Tool}: tipo {ExceptionType}, detalhe {Detail}")]
    private partial void LogToolFailureMessage(string tool, string exceptionType, string detail);

    // Sem stack trace e sem caminhos: a mensagem passa pela mesma redação das respostas ao cliente.
    private void LogToolFailure(Exception exception, string tool) =>
        LogToolFailureMessage(tool, exception.GetType().Name, McpOutcome.Sanitize(exception.Message));

    private void LogMutationResult(string tool, Guid projectId, string key, McpIdempotencyResult result) =>
        LogMutationOutcome(tool, projectId, key, result.Outcome.IsError ? "erro" : "ok",
            result.Outcome.ErrorCode, result.Reused);

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,100}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdempotencyKeyPattern();
}
