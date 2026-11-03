using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;

namespace TravelCut.Application.Ports;

public interface IJobQueue
{
    Task<ProcessingJob> EnqueueAsync(Guid projectId, Domain.Enums.JobType type, CancellationToken ct = default);
    Task UpdateProgressAsync(Guid jobId, int completed, int total, string stepDescription, CancellationToken ct = default);
    Task MarkCompletedAsync(Guid jobId, CancellationToken ct = default);
    Task MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken ct = default);
    Task<ProcessingJob?> GetJobAsync(Guid jobId, CancellationToken ct = default);
    Task<List<ProcessingJob>> ListJobsByProjectAsync(Guid projectId, CancellationToken ct = default);
}

/// <summary>
/// Adaptador de editor (F08, contrato editor/1). Implementações: MockResolveAdapter (SIMULATED, sem .drp) e
/// DaVinciResolveAdapter (bridge Python real; nunca cai no mock em silêncio). Toda mutação acontece em um projeto
/// NOVO criado pelo próprio adaptador a partir do manifesto; projetos existentes do usuário nunca são abertos para escrita.
/// </summary>
public interface IEditorAdapter
{
    /// <summary>"mock" | "davinci".</summary>
    string Mode { get; }

    Task<EditorCapabilitiesDto> DetectCapabilitiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Compõe create_project → import_media → create_timeline → apply_edit_plan → query_timeline (→ create_snapshot),
    /// relatando cada etapa. Não lança para falhas do editor: devolve Status ERROR/UNAVAILABLE com código.
    /// </summary>
    Task<EditorAdapterApplyResult> ApplyAssemblyAsync(EditorAssemblyDto assembly, EditorApplyOptions options, CancellationToken ct = default);

    Task<EditorTimelineQueryDto> QueryTimelineAsync(string editorProjectName, string timelineName, CancellationToken ct = default);

    /// <summary>Exporta o projeto nativo (.drp) para um caminho absoluto sob 3-Trabalho/editor. Mock: UNAVAILABLE (não gera .drp).</summary>
    Task<EditorExportResultDto> ExportProjectAsync(string editorProjectName, string outputAbsolutePath, CancellationToken ct = default);

    /// <summary>Renderiza prévia MP4 da timeline para um diretório sob 3-Trabalho/editor. Mock: UNAVAILABLE.</summary>
    Task<EditorExportResultDto> ExportPreviewAsync(string editorProjectName, string timelineName, string outputDirectoryAbsolute, CancellationToken ct = default);
}

/// <param name="SnapshotAbsolutePath">Destino do .drp exportado logo após aplicar (recuperação); null = não exportar.</param>
/// <param name="Timeout">Limite da aplicação.</param>
/// <param name="ManifestAbsolutePath">Caminho absoluto do manifesto já gravado em 3-Trabalho/editor (o adaptador o usa sem gravar cópia
/// temporária). null = o adaptador grava um temporário próprio (legado).</param>
public sealed record EditorApplyOptions(string? SnapshotAbsolutePath, TimeSpan Timeout, string? ManifestAbsolutePath = null);

public sealed record EditorAdapterApplyResult(
    string Status,                              // EditorResultStatus
    string? EditorVersion,
    IReadOnlyList<EditorStepResultDto> Steps,
    IReadOnlyList<EditorClipMappingDto> ClipMappings,
    IReadOnlyList<EditorRejectedOpDto> Rejected, // rejeições do editor (somadas às do manifesto)
    IReadOnlyList<EditorWarningDto> Warnings,
    string? SnapshotAbsolutePath,               // preenchido só se o .drp foi realmente exportado
    string? ErrorCode,
    string? ErrorMessage);
