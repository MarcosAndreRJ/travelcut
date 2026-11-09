using Microsoft.Extensions.Logging;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;

namespace TravelCut.Application.Planning;

/// <summary>Perfil de automação editorial persistido por projeto (coluna Projects.AutomationProfile).</summary>
public sealed partial class PlanningUseCases
{
    /// <summary>Lê o perfil persistido; projeto antigo/sem escolha volta "balanced" com isDefault=true.</summary>
    public async Task<PlanOutcome<AutomationProfileDto>> GetAutomationProfileAsync(
        GetAutomationProfileRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<AutomationProfileDto>.Fail("INVALID_ARGUMENT", "ID de projeto inválido.");

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null)
            return PlanOutcome<AutomationProfileDto>.Fail("PROJECT_NOT_FOUND", NoProjectMessage);

        return PlanOutcome<AutomationProfileDto>.Ok(ToAutomationProfileDto(project));
    }

    /// <summary>
    /// Grava o perfil do projeto. Valor fora do vocabulário é rejeitado (nunca vira "balanced" silenciosamente).
    /// Serializado com a geração do plano pelo lock do projeto. A revisão vigente passa a PLAN_OUTDATED
    /// (o perfil faz parte do fingerprint de entradas) até um novo plan.generate.
    /// </summary>
    public async Task<PlanOutcome<AutomationProfileDto>> SetAutomationProfileAsync(
        SetAutomationProfileRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<AutomationProfileDto>.Fail("INVALID_ARGUMENT", "ID de projeto inválido.");
        if (!AutoEditorialProfiles.TryParse(request.Profile, out var parsed))
            return PlanOutcome<AutomationProfileDto>.Fail("INVALID_ARGUMENT",
                "Perfil de automação inválido (use conservative, balanced ou aggressive).");

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null)
            return PlanOutcome<AutomationProfileDto>.Fail("PROJECT_NOT_FOUND", NoProjectMessage);

        return await WithProjectLockAsync(project.ProjectId, async () =>
        {
            project.AutomationProfile = AutoEditorialProfiles.ToName(parsed);
            project.LastModifiedAtUtc = _clock.GetUtcNow().UtcDateTime;
            await _projects.SaveAsync(project, ct);
            _logger.LogInformation("Projeto {ProjectId}: perfil de automação definido como {Profile}",
                project.ProjectId, project.AutomationProfile);
            return PlanOutcome<AutomationProfileDto>.Ok(ToAutomationProfileDto(project));
        }, ct);
    }

    private static AutomationProfileDto ToAutomationProfileDto(Project project)
    {
        bool chosen = AutoEditorialProfiles.TryParse(project.AutomationProfile, out var parsed);
        return new AutomationProfileDto(
            project.ProjectId,
            AutoEditorialProfiles.ToName(chosen ? parsed : AutoEditorialProfiles.Default),
            IsDefault: !chosen);
    }

    /// <summary>Perfil editorial padrão com o perfil de automação persistido do projeto (null/inválido → balanced).</summary>
    private static EditorialProfileDto ProfileForProject(Project project) =>
        EditorialProfiles.Default() with
        {
            SilencePolicy = AutoEditorialProfiles.ToName(
                AutoEditorialProfiles.TryParse(project.AutomationProfile, out var parsed) ? parsed : AutoEditorialProfiles.Default),
        };
}
