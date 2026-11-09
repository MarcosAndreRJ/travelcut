using Microsoft.Extensions.Logging;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;

namespace TravelCut.Application.Planning;

/// <summary>Opções de automação por projeto (coluna Projects.AutoProcess): gate do processamento pós-análise.</summary>
public sealed partial class PlanningUseCases
{
    /// <summary>Lê o gate; projeto antigo/sem escolha volta AutoProcess=true com isDefault=true.</summary>
    public async Task<PlanOutcome<AutomationOptionsDto>> GetAutomationOptionsAsync(
        GetAutomationOptionsRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<AutomationOptionsDto>.Fail("INVALID_ARGUMENT", "ID de projeto inválido.");

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null)
            return PlanOutcome<AutomationOptionsDto>.Fail("PROJECT_NOT_FOUND", NoProjectMessage);

        return PlanOutcome<AutomationOptionsDto>.Ok(ToAutomationOptionsDto(project));
    }

    public async Task<PlanOutcome<AutomationOptionsDto>> SetAutomationOptionsAsync(
        SetAutomationOptionsRequest request, CancellationToken ct = default)
    {
        if (request.ProjectId == Guid.Empty)
            return PlanOutcome<AutomationOptionsDto>.Fail("INVALID_ARGUMENT", "ID de projeto inválido.");

        var project = await _projects.GetByIdAsync(request.ProjectId, ct);
        if (project is null)
            return PlanOutcome<AutomationOptionsDto>.Fail("PROJECT_NOT_FOUND", NoProjectMessage);

        return await WithProjectLockAsync(project.ProjectId, async () =>
        {
            project.AutoProcess = request.AutoProcess;
            project.LastModifiedAtUtc = _clock.GetUtcNow().UtcDateTime;
            await _projects.SaveAsync(project, ct);
            _logger.LogInformation("Projeto {ProjectId}: processamento automático pós-análise {State}",
                project.ProjectId, request.AutoProcess ? "ligado" : "desligado");
            return PlanOutcome<AutomationOptionsDto>.Ok(ToAutomationOptionsDto(project));
        }, ct);
    }

    private static AutomationOptionsDto ToAutomationOptionsDto(Project project) =>
        new(project.ProjectId, project.AutoProcess ?? true, IsDefault: project.AutoProcess is null);
}
