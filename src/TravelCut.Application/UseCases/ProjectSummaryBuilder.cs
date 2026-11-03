using TravelCut.Application.Planning;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;

namespace TravelCut.Application.UseCases;

/// <summary>
/// Único ponto que monta <see cref="ProjectSummaryDto"/> (project.open, project.create, project.recent).
/// Regras:
/// - Duração total soma somente mídias com MetadataStatus "ok"; as demais são contadas em MediaWithoutMetadataCount.
/// - Estimativa de corte não existe ainda: sempre null (nunca derivada de constante — ver CLAUDE.md "não inventar duração").
/// </summary>
public static class ProjectSummaryBuilder
{
    public static ProjectSummaryDto Build(
        Project project,
        IReadOnlyList<MediaAsset> media,
        IReadOnlyList<EditSuggestion> suggestions,
        IReadOnlyList<EditDecision> decisions)
    {
        bool fourFoldersValid = project.HasValidFourFoldersStructure(out string missingFolder);

        // approvedCutsCount = sugestões cujo estado efetivo é Approved (mesma regra do planejador).
        int approved = EffectiveSuggestionStatus.CountApproved(suggestions, decisions);

        var withMetadata = media.Where(HasOkMetadata).ToList();
        double totalDuration = withMetadata.Sum(m => m.Metadata!.DurationSeconds);

        return new ProjectSummaryDto(
            ProjectId: project.ProjectId,
            Name: project.Name,
            RootPath: project.RootPath,
            FourFoldersValid: fourFoldersValid,
            MissingFolder: fourFoldersValid ? null : missingFolder,
            MediaCount: media.Count,
            SuggestionsCount: suggestions.Count,
            ApprovedCutsCount: approved,
            TotalOriginalDurationSeconds: totalDuration,
            EstimatedCutDurationSeconds: null,
            MediaWithoutMetadataCount: media.Count - withMetadata.Count,
            LastModifiedUtc: project.LastModifiedAtUtc);
    }

    /// <summary>Carrega mídias, sugestões e decisões do repositório e monta o resumo.</summary>
    public static async Task<ProjectSummaryDto> BuildAsync(
        Project project,
        IMediaRepository mediaRepo,
        IEditPlanRepository planRepo,
        CancellationToken ct = default)
    {
        var media = await mediaRepo.ListByProjectAsync(project.ProjectId, ct);
        var suggestions = await planRepo.ListSuggestionsByProjectAsync(project.ProjectId, ct);
        var decisions = await planRepo.ListDecisionsByProjectAsync(project.ProjectId, ct);
        return Build(project, media, suggestions, decisions);
    }

    private static bool HasOkMetadata(MediaAsset asset) =>
        asset.Metadata is { MetadataStatus: MediaMetadataStatus.Ok };
}
