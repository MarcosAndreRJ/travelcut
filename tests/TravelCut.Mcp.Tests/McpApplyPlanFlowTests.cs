using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Mcp.Facade;
using TravelCut.Mcp.Tests.Support;
using Xunit;

namespace TravelCut.Mcp.Tests;

/// <summary>
/// Fluxo feliz de apply_plan pelo MCP, em modo simulador do editor (MockResolveAdapter): aprovação via submit_review,
/// plano pelo IPC (plan.generate), dry-run → fingerprint → aplicação com confirmação. Também fingerprint divergente.
/// Mídias de teste existem em disco e têm metadados de FFprobe, como exigido pelo planejador.
/// </summary>
public sealed class McpApplyPlanFlowTests : IDisposable
{
    private readonly McpTestHost _host;
    private readonly string _projectA = McpTestHost.ProjectA.ToString();

    public McpApplyPlanFlowTests()
    {
        _host = McpTestHost.Create(allowMutations: true, McpTestHost.ProjectA);
        _host.SeedAsync().GetAwaiter().GetResult();
        PrepareMediaAndMetadataAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _host.Dispose();

    private async Task PrepareMediaAndMetadataAsync()
    {
        var bruto = Path.Combine(_host.Root, "A", "1-Bruto");
        Directory.CreateDirectory(bruto);
        File.WriteAllBytes(Path.Combine(bruto, "clip1.mp4"), new byte[64]);
        File.WriteAllBytes(Path.Combine(bruto, "clip2.mp4"), new byte[64]);

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelCutDbContext>();
        foreach (var mediaId in new[] { McpTestHost.MediaA, McpTestHost.MediaA2 })
        {
            db.MediaMetadata.Add(new MediaMetadata
            {
                MediaId = mediaId,
                Codec = "h264",
                Width = 1920,
                Height = 1080,
                FrameRate = 29.97,
                DurationSeconds = 60,
                DurationTicks = 60L * TimeSpan.TicksPerSecond,
                AudioChannels = 2,
                AudioSampleRate = 48000,
                SourceDateEvidence = "teste",
                MetadataStatus = MediaMetadataStatus.Ok,
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task ApproveAndPlanAsync()
    {
        var a1 = await _host.Mutations.SubmitReview(_projectA, McpTestHost.SuggestionA.ToString(), "approve", "Revisor", "apr-a1");
        var a2 = await _host.Mutations.SubmitReview(_projectA, McpTestHost.SuggestionA2.ToString(), "approve", "Revisor", "apr-a2");
        Assert.False(ToolJson.IsError(a1));
        Assert.False(ToolJson.IsError(a2));

        var facade = _host.Services.GetRequiredService<TravelCutMcpFacade>();
        var plan = await facade.DispatchAsync("plan.generate", new GeneratePlanRequest(McpTestHost.ProjectA, null, null), CancellationToken.None);
        Assert.True(plan.Ok, plan.Error?.Message);
    }

    [Fact]
    public async Task DryRunDepoisAplicacaoComFingerprintEConfirmacao_FuncionaNoModoSimulado()
    {
        await ApproveAndPlanAsync();

        var dry = await _host.Mutations.ApplyPlan(_projectA, "dry-flow-1", dryRun: true);
        Assert.False(ToolJson.IsError(dry), ToolJson.Text(dry));
        var preview = ToolJson.Parse(dry).AsObject();
        Assert.Equal("DRY_RUN", preview["status"]!.GetValue<string>());
        Assert.True(preview["canApply"]!.GetValue<bool>(), ToolJson.Text(dry));
        var fingerprint = preview["assemblyFingerprint"]!.GetValue<string>();
        var revision = preview["revision"]!.GetValue<int>();
        Assert.False(string.IsNullOrWhiteSpace(fingerprint));

        var apply = await _host.Mutations.ApplyPlan(
            _projectA, "apply-flow-1", revision: revision, dryRun: false,
            assemblyFingerprint: fingerprint, confirmProjectSwitch: true);

        Assert.False(ToolJson.IsError(apply), ToolJson.Text(apply));
        var result = ToolJson.Parse(apply).AsObject();
        Assert.Equal("SIMULATED", result["status"]!.GetValue<string>());
        Assert.True(result["isSimulated"]!.GetValue<bool>());
        Assert.DoesNotContain(_host.Root, ToolJson.Text(apply), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AplicacaoRepetidaComAMesmaChave_DevolveOMesmoResultadoSemReexecutar()
    {
        await ApproveAndPlanAsync();
        var dry = ToolJson.Parse(await _host.Mutations.ApplyPlan(_projectA, "dry-flow-2", dryRun: true)).AsObject();
        var fingerprint = dry["assemblyFingerprint"]!.GetValue<string>();
        var revision = dry["revision"]!.GetValue<int>();

        var first = await _host.Mutations.ApplyPlan(_projectA, "apply-flow-2", revision: revision, dryRun: false,
            assemblyFingerprint: fingerprint, confirmProjectSwitch: true);
        var second = await _host.Mutations.ApplyPlan(_projectA, "apply-flow-2", revision: revision, dryRun: false,
            assemblyFingerprint: fingerprint, confirmProjectSwitch: true);

        Assert.False(ToolJson.IsError(first));
        Assert.Equal(ToolJson.Text(first), ToolJson.Text(second));
    }

    [Fact]
    public async Task FingerprintDivergente_EhRecusadoComoVersaoEmConflito()
    {
        await ApproveAndPlanAsync();
        var dry = ToolJson.Parse(await _host.Mutations.ApplyPlan(_projectA, "dry-flow-3", dryRun: true)).AsObject();
        var revision = dry["revision"]!.GetValue<int>();

        var result = await _host.Mutations.ApplyPlan(_projectA, "apply-flow-3", revision: revision, dryRun: false,
            assemblyFingerprint: "fingerprint-que-nao-existe", confirmProjectSwitch: true);

        Assert.True(ToolJson.IsError(result));
        Assert.Equal("PROJECT_VERSION_CONFLICT", ToolJson.Parse(result)["code"]!.GetValue<string>());
    }
}
