using System.Text.Json.Serialization;

namespace TravelCut.Contracts.Dtos;

// Contrato motion/1 (F09.5, HyperFrames opcional). Regras (docs/20-HYPERFRAMES.md, docs/05, docs/12):
// - Animações de título/lower third/rota são renderizadas por um worker Node isolado; o núcleo nunca depende de Node/HyperFrames.
// - Capacidade declarada por estado real: NODE_UNAVAILABLE | NOT_INSTALLED | READY | MOCK | ERROR. Nada é afirmado sem leitura real.
// - "mock" é SIMULATED (isSimulated=true) e NUNCA gera arquivo de vídeo (outputRelativePath é sempre null).
// - Verificação por template: documented | mock_pass | host_pending | host_pass | unsupported (reutiliza CapabilityVerification de EditorDtos).
//   Nenhum template é "host_pass" nesta VM. rendererVersion só é preenchida quando o CLI respondeu à leitura de versão.
// - Assets de mídia só são aceitos a partir de 2-Assets do projeto (somente leitura) e precisam existir.
// - Saídas e manifestos só em 3-Trabalho/motion/<renderId>/. Caminhos relativos ao projeto; nunca absolutos em mensagens.
// - Idempotência por idempotencyKey: mesma chave + mesmo pedido = resultado guardado; mesma chave + pedido diferente = conflito.
// - Canais: IPC/MCP acionam os mesmos casos de uso (MotionUseCases); não há rota paralela de render.

public static class MotionSchemaVersions
{
    public const string Capabilities = "motion/1";
    public const string Request = "motion-request/1";
    public const string Result = "motion-result/1";
}

public static class MotionAdapterModes
{
    public const string Mock = "mock";
    public const string HyperFrames = "hyperframes";
}

public static class MotionEnvironmentStates
{
    public const string NodeUnavailable = "NODE_UNAVAILABLE";  // node ausente, inacessível ou com erro na checagem
    public const string NotInstalled = "NOT_INSTALLED";        // TRAVELCUT_HYPERFRAMES_CLI ausente/inexistente
    public const string Ready = "READY";                        // CLI presente e versão lida (formato de render: host_pending)
    public const string Mock = "MOCK";
    public const string Error = "ERROR";                        // CLI presente mas responde de forma inesperada
}

public static class MotionTemplateIds
{
    public const string TitleCard = "title_card";
    public const string LowerThird = "lower_third";
    public const string MapRoute = "map_route";
}

public static class MotionRenderStatuses
{
    public const string Simulated = "SIMULATED";   // mock: demonstração, sem arquivo
    public const string ReadyReal = "READY_REAL";  // CLI real terminou com código 0 e arquivo de saída existe
    public const string Unavailable = "UNAVAILABLE";
    public const string Error = "ERROR";
}

public record MotionTemplateCapabilityDto(
    [property: JsonPropertyName("templateId")] string TemplateId,       // MotionTemplateIds
    [property: JsonPropertyName("supported")] bool Supported,           // pode ser solicitado agora neste ambiente
    [property: JsonPropertyName("verification")] string Verification,   // CapabilityVerification
    [property: JsonPropertyName("note")] string? Note
);

public record MotionCapabilitiesDto(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,       // "motion/1"
    [property: JsonPropertyName("adapterMode")] string AdapterMode,           // "mock" | "hyperframes"
    [property: JsonPropertyName("environmentState")] string EnvironmentState, // MotionEnvironmentStates
    [property: JsonPropertyName("isAvailable")] bool IsAvailable,
    [property: JsonPropertyName("isSimulated")] bool IsSimulated,
    [property: JsonPropertyName("nodeVersion")] string? NodeVersion,          // só se lida do node --version
    [property: JsonPropertyName("rendererVersion")] string? RendererVersion,  // só se lida do CLI --version; nunca inventada
    [property: JsonPropertyName("statusMessage")] string StatusMessage,
    [property: JsonPropertyName("reasonCode")] string? ReasonCode,            // NODE_NOT_FOUND | CLI_PATH_NOT_FOUND | CLI_VERSION_FAILED | ...
    [property: JsonPropertyName("templates")] List<MotionTemplateCapabilityDto> Templates,
    [property: JsonPropertyName("checkedAtUtc")] DateTime CheckedAtUtc
);

public record MotionRenderRequestDto(
    [property: JsonPropertyName("projectId")] Guid ProjectId,
    [property: JsonPropertyName("templateId")] string TemplateId,             // MotionTemplateIds
    [property: JsonPropertyName("parameters")] Dictionary<string, string> Parameters, // validado por template (MotionTemplateCatalog)
    [property: JsonPropertyName("durationFrames")] long DurationFrames,
    [property: JsonPropertyName("frameRateNum")] long FrameRateNum,
    [property: JsonPropertyName("frameRateDen")] long FrameRateDen,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("idempotencyKey")] string IdempotencyKey      // 8..128 caracteres [A-Za-z0-9._-]
);

public record MotionRenderResultDto(
    [property: JsonPropertyName("status")] string Status,                       // MotionRenderStatuses
    [property: JsonPropertyName("isSimulated")] bool IsSimulated,
    [property: JsonPropertyName("outputRelativePath")] string? OutputRelativePath, // NUNCA preenchido no mock; só READY_REAL com arquivo existente
    [property: JsonPropertyName("manifestRelativePath")] string ManifestRelativePath, // vazio só quando o pedido foi recusado antes de gerar manifesto
    [property: JsonPropertyName("errorCode")] string? ErrorCode,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("startedAtUtc")] DateTime StartedAtUtc,
    [property: JsonPropertyName("finishedAtUtc")] DateTime FinishedAtUtc
);
