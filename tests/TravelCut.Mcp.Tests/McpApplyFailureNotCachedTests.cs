using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Mcp.Facade;
using TravelCut.Mcp.Tests.Support;
using Xunit;

namespace TravelCut.Mcp.Tests;

/// <summary>
/// M3: apply_plan cujo registro volta com status ERROR (o IPC devolve sucesso técnico) não entra no cache de idempotência:
/// a mesma chave, repetida, executa o adaptador de novo.
/// </summary>
public sealed class McpApplyFailureNotCachedTests : IDisposable
{
    private readonly McpTestHost _host;
    private readonly string _projectA = McpTestHost.ProjectA.ToString();
    private FailingEditorAdapter? _failing;

    public McpApplyFailureNotCachedTests()
    {
        _host = McpTestHost.CreateWithEditor(inner => _failing = new FailingEditorAdapter(inner), McpTestHost.ProjectA);
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
        var a1 = await _host.Mutations.SubmitReview(_projectA, McpTestHost.SuggestionA.ToString(), "approve", "Revisor", "apr-err-a1");
        var a2 = await _host.Mutations.SubmitReview(_projectA, McpTestHost.SuggestionA2.ToString(), "approve", "Revisor", "apr-err-a2");
        Assert.False(ToolJson.IsError(a1));
        Assert.False(ToolJson.IsError(a2));

        var facade = _host.Services.GetRequiredService<TravelCutMcpFacade>();
        var plan = await facade.DispatchAsync("plan.generate", new GeneratePlanRequest(McpTestHost.ProjectA, null, null), CancellationToken.None);
        Assert.True(plan.Ok, plan.Error?.Message);
    }

    [Fact]
    public async Task ApplyComStatusError_NaoEhCacheado_MesmaChaveExecutaDeNovo()
    {
        await ApproveAndPlanAsync();
        var dry = ToolJson.Parse(await _host.Mutations.ApplyPlan(_projectA, "dry-err-1", dryRun: true)).AsObject();
        var fingerprint = dry["assemblyFingerprint"]!.GetValue<string>();
        var revision = dry["revision"]!.GetValue<int>();

        var first = await ApplyAsync("apply-err-1", revision, fingerprint);
        var second = await ApplyAsync("apply-err-1", revision, fingerprint);

        Assert.Equal("ERROR", first["status"]!.GetValue<string>());
        Assert.Equal("ERROR", second["status"]!.GetValue<string>());
        Assert.Equal(2, _failing!.ApplyCalls);
    }

    private async Task<JsonNode> ApplyAsync(string key, int revision, string fingerprint)
    {
        var result = await _host.Mutations.ApplyPlan(_projectA, key, revision: revision, dryRun: false,
            assemblyFingerprint: fingerprint, confirmProjectSwitch: true);
        Assert.False(ToolJson.IsError(result), ToolJson.Text(result));
        return ToolJson.Parse(result);
    }

    /// <summary>Adaptador que falha no apply (exceção do adaptador → registro ERROR), delegando o resto ao simulador.</summary>
    private sealed class FailingEditorAdapter(IEditorAdapter inner) : IEditorAdapter
    {
        private int _applyCalls;

        public int ApplyCalls => Volatile.Read(ref _applyCalls);

        public string Mode => inner.Mode;

        public Task<EditorCapabilitiesDto> DetectCapabilitiesAsync(CancellationToken ct = default) =>
            inner.DetectCapabilitiesAsync(ct);

        public Task<EditorAdapterApplyResult> ApplyAssemblyAsync(EditorAssemblyDto assembly, EditorApplyOptions options, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _applyCalls);
            throw new InvalidOperationException("adaptador indisponível (teste)");
        }

        public Task<EditorTimelineQueryDto> QueryTimelineAsync(string editorProjectName, string timelineName, CancellationToken ct = default) =>
            inner.QueryTimelineAsync(editorProjectName, timelineName, ct);

        public Task<EditorExportResultDto> ExportProjectAsync(string editorProjectName, string outputAbsolutePath, CancellationToken ct = default) =>
            inner.ExportProjectAsync(editorProjectName, outputAbsolutePath, ct);

        public Task<EditorExportResultDto> ExportPreviewAsync(string editorProjectName, string timelineName, string outputDirectoryAbsolute, CancellationToken ct = default) =>
            inner.ExportPreviewAsync(editorProjectName, timelineName, outputDirectoryAbsolute, ct);
    }
}
