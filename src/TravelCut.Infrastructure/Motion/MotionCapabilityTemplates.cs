using TravelCut.Application.Motion;
using TravelCut.Contracts.Dtos;

namespace TravelCut.Infrastructure.Motion;

/// <summary>Lista de capacidades por template a partir do catálogo, com a verificação correta por adaptador.</summary>
internal static class MotionCapabilityTemplates
{
    public static List<MotionTemplateCapabilityDto> Build(bool supported, string verification, string? note) =>
        MotionTemplateCatalog.Templates
            .Select(t => new MotionTemplateCapabilityDto(t.TemplateId, supported, verification, note))
            .ToList();
}
