using TravelCut.Application.Audio;
using TravelCut.Application.Ports;
using TravelCut.Contracts.Dtos;
using TravelCut.Domain.Entities;

namespace TravelCut.UnitTests.Planning;

/// <summary>Índice de revisões em memória (sem banco) para testes de IPC.</summary>
public sealed class FakePlanRevisionRepository : IPlanRevisionRepository
{
    public readonly List<PlanRevisionIndexEntry> Entries = [];

    public Task<PlanRevisionIndexEntry?> GetLatestAsync(Guid projectId, CancellationToken ct = default) =>
        Task.FromResult(Entries.Where(e => e.ProjectId == projectId).OrderByDescending(e => e.Revision).FirstOrDefault());

    public Task<PlanRevisionIndexEntry?> GetAsync(Guid projectId, int revision, CancellationToken ct = default) =>
        Task.FromResult(Entries.FirstOrDefault(e => e.ProjectId == projectId && e.Revision == revision));

    public Task<List<PlanRevisionIndexEntry>> ListAsync(Guid projectId, CancellationToken ct = default) =>
        Task.FromResult(Entries.Where(e => e.ProjectId == projectId).OrderBy(e => e.Revision).ToList());

    public Task AddAsync(PlanRevisionIndexEntry entry, CancellationToken ct = default)
    {
        if (Entries.Any(e => e.ProjectId == entry.ProjectId && e.Revision == entry.Revision))
            throw new InvalidOperationException("UNIQUE(ProjectId, Revision) violado.");
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task UpdateInputsFingerprintAsync(Guid projectId, int revision, string inputsFingerprint, CancellationToken ct = default)
    {
        int i = Entries.FindIndex(e => e.ProjectId == projectId && e.Revision == revision);
        if (i >= 0) Entries[i] = Entries[i] with { InputsFingerprint = inputsFingerprint };
        return Task.CompletedTask;
    }
}

/// <summary>Armazenamento de artefatos em memória; replica a regra de não sobrescrever.</summary>
public sealed class InMemoryPlanArtifactStore : IPlanArtifactStore
{
    private readonly Dictionary<(Guid PlanId, int Revision), string> _files = new();

    public IReadOnlyDictionary<(Guid PlanId, int Revision), string> Files => _files;

    /// <summary>Falha injetada na próxima gravação (ex.: disco cheio): lançada como está.</summary>
    public Exception? WriteFailure { get; set; }

    public Task WriteRevisionAsync(string projectRoot, Guid planId, int revision, string json, CancellationToken ct = default)
    {
        if (WriteFailure is not null) throw WriteFailure;
        if (_files.ContainsKey((planId, revision))) throw new PlanRevisionConflictException("Revisão já existe.");
        _files[(planId, revision)] = json;
        return Task.CompletedTask;
    }

    /// <summary>Substitui o conteúdo de uma revisão já gravada (simula artefato antigo/legado). Só para testes.</summary>
    public void Replace(Guid planId, int revision, string json) => _files[(planId, revision)] = json;

    public Task<string?> ReadRevisionAsync(string projectRoot, Guid planId, int revision, CancellationToken ct = default) =>
        Task.FromResult(_files.TryGetValue((planId, revision), out var json) ? json : null);

    public Task<IReadOnlyList<int>> ListRevisionNumbersAsync(string projectRoot, Guid planId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<int>>(_files.Keys.Where(k => k.PlanId == planId).Select(k => k.Revision).OrderBy(n => n).ToList());

    public Task DeleteUncommittedRevisionAsync(string projectRoot, Guid planId, int revision, CancellationToken ct = default)
    {
        _files.Remove((planId, revision));
        return Task.CompletedTask;
    }

    /// <summary>Simula arquivo removido por fora do TravelCut.</summary>
    public void Remove(Guid planId, int revision) => _files.Remove((planId, revision));
}

/// <summary>Análises por mídia em memória (sem cache de disco nem Whisper).</summary>
public sealed class FakePipelineAnalysis : IPipelineJobManager
{
    public Dictionary<Guid, MediaAnalysisDto> Analyses { get; } = new();

    public Task<MediaAnalysisDto?> GetMediaAnalysisAsync(Guid projectId, Guid mediaId, CancellationToken ct = default) =>
        Task.FromResult(Analyses.TryGetValue(mediaId, out var a) ? a : null);

    public Task<Guid> StartAsync(StartPipelineRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException("Não usado nos testes de plano.");

    public Task<PipelineStatusDto> GetStatusAsync(Guid projectId, CancellationToken ct = default) =>
        throw new NotSupportedException("Não usado nos testes de plano.");

    public bool Cancel(Guid jobId) => throw new NotSupportedException("Não usado nos testes de plano.");

    public Task<AnalysisEnvironmentDto> GetEnvironmentAsync(CancellationToken ct = default, bool forceRefresh = false) =>
        throw new NotSupportedException("Não usado nos testes de plano.");
}

/// <summary>Existência de arquivo controlada pelo teste: por padrão todos existem; <see cref="Missing"/> lista os ausentes.</summary>
public sealed class FakeFileExistence : TravelCut.Application.Planning.IFileExistence
{
    public HashSet<string> Missing { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> ExistsAsync(string fullPath, CancellationToken ct = default) =>
        Task.FromResult(!Missing.Contains(fullPath));
}
