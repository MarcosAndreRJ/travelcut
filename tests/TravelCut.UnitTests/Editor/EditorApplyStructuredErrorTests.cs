using Microsoft.Extensions.Logging.Abstractions;
using TravelCut.Application.Editor;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Infrastructure.Repositories;
using TravelCut.UnitTests.Planning;
using Xunit;

namespace TravelCut.UnitTests.Editor;

/// <summary>
/// Aplicação no editor com falhas de infraestrutura (revisão de concorrência):
/// B1 (trava com raiz ausente vira erro estruturado, sem adaptador) e
/// M2 (adaptador cancelado ainda grava o registro EDITOR_CANCELLED).
/// </summary>
public sealed class EditorApplyStructuredErrorTests : IDisposable
{
    private readonly EditorFixture _f = new();

    public void Dispose() => _f.Dispose();

    private static ApplyEditorPlanRequest ApplyRequest(EditorApplyPreviewDto preview, string key) =>
        new(TravelFixture.ProjectId, preview.Revision, preview.AssemblyFingerprint, false, key);

    private async Task<EditorApplyPreviewDto> PreparedAsync()
    {
        await _f.GeneratePlanAsync();
        var r = await _f.Editor.PrepareApplyAsync(new PrepareEditorApplyRequest(TravelFixture.ProjectId, null));
        Assert.True(r.Success, r.ErrorMessage);
        return r.Value!;
    }

    [Fact]
    public async Task PastaDoProjetoRemovida_ApplyDevolveProjectRootMissing_SemChamarAdaptador()
    {
        var preview = await PreparedAsync();
        Directory.Delete(_f.Root, recursive: true);

        var r = await _f.Editor.ApplyAsync(ApplyRequest(preview, "raiz-ausente-1"));

        Assert.False(r.Success);
        Assert.Equal(ProjectLockException.ProjectRootMissingCode, r.ErrorCode);
        Assert.DoesNotContain(_f.Root, r.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _f.Adapter.ApplyCalls);
    }

    [Fact]
    public async Task AdaptadorCancelado_AindaGravaORegistroEditorCancelled()
    {
        var preview = await PreparedAsync();
        using var cts = new CancellationTokenSource();
        var cancelling = new CancellingEditorAdapter(_f.Adapter, cts);
        var editor = new EditorUseCases(
            _f.Projects, _f.Media, _f.Planning, cancelling, _f.EditorStore, new FileSystemFileExistence(),
            _f.Locks, TimeProvider.System, NullLogger<EditorUseCases>.Instance);

        var r = await editor.ApplyAsync(ApplyRequest(preview, "cancelado-1"), cts.Token);

        Assert.True(r.Success, r.ErrorMessage);
        Assert.Equal(EditorUseCases.CancelledCode, r.Value!.ErrorCode);
        Assert.Equal(EditorResultStatus.Error, r.Value.Status);

        var registros = await _f.EditorStore.ListApplicationsAsync(_f.Root, CancellationToken.None);
        Assert.Contains(registros, a => a.ErrorCode == EditorUseCases.CancelledCode && a.IdempotencyKey == "cancelado-1");
    }

    /// <summary>Adaptador que cancela o token da aplicação e lança OperationCanceledException (como o adaptador real faria).</summary>
    private sealed class CancellingEditorAdapter(IEditorAdapter inner, CancellationTokenSource cts) : IEditorAdapter
    {
        public string Mode => inner.Mode;

        public Task<EditorCapabilitiesDto> DetectCapabilitiesAsync(CancellationToken ct = default) =>
            inner.DetectCapabilitiesAsync(ct);

        public Task<EditorAdapterApplyResult> ApplyAssemblyAsync(EditorAssemblyDto assembly, EditorApplyOptions options, CancellationToken ct = default)
        {
            cts.Cancel();
            throw new OperationCanceledException("cancelado durante a aplicação (teste)", ct);
        }

        public Task<EditorTimelineQueryDto> QueryTimelineAsync(string editorProjectName, string timelineName, CancellationToken ct = default) =>
            inner.QueryTimelineAsync(editorProjectName, timelineName, ct);

        public Task<EditorExportResultDto> ExportProjectAsync(string editorProjectName, string outputAbsolutePath, CancellationToken ct = default) =>
            inner.ExportProjectAsync(editorProjectName, outputAbsolutePath, ct);

        public Task<EditorExportResultDto> ExportPreviewAsync(string editorProjectName, string timelineName, string outputDirectoryAbsolute, CancellationToken ct = default) =>
            inner.ExportPreviewAsync(editorProjectName, timelineName, outputDirectoryAbsolute, ct);
    }
}
