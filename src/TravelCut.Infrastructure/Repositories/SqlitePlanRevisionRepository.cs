using Microsoft.EntityFrameworkCore;
using TravelCut.Application.Ports;
using TravelCut.Infrastructure.Persistence;
using TravelCut.Infrastructure.Planning;

namespace TravelCut.Infrastructure.Repositories;

/// <summary>Índice das revisões do plano. Somente inserções (revisões são imutáveis).</summary>
public class SqlitePlanRevisionRepository : IPlanRevisionRepository
{
    private readonly TravelCutDbContext _db;

    public SqlitePlanRevisionRepository(TravelCutDbContext db)
    {
        _db = db;
    }

    public async Task<PlanRevisionIndexEntry?> GetLatestAsync(Guid projectId, CancellationToken ct = default)
    {
        var record = await _db.PlanRevisions
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.Revision)
            .FirstOrDefaultAsync(ct);
        return record is null ? null : ToEntry(record);
    }

    public async Task<PlanRevisionIndexEntry?> GetAsync(Guid projectId, int revision, CancellationToken ct = default)
    {
        var record = await _db.PlanRevisions
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProjectId == projectId && r.Revision == revision, ct);
        return record is null ? null : ToEntry(record);
    }

    public async Task<List<PlanRevisionIndexEntry>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        var records = await _db.PlanRevisions
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderBy(r => r.Revision)
            .ToListAsync(ct);
        return records.Select(ToEntry).ToList();
    }

    public async Task UpdateInputsFingerprintAsync(Guid projectId, int revision, string inputsFingerprint, CancellationToken ct = default)
    {
        await _db.PlanRevisions
            .Where(r => r.ProjectId == projectId && r.Revision == revision)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.InputsFingerprint, inputsFingerprint), ct);
    }

    public async Task AddAsync(PlanRevisionIndexEntry entry, CancellationToken ct = default)
    {
        var record = new PlanRevisionRecord
        {
            PlanId = entry.PlanId,
            Revision = entry.Revision,
            ProjectId = entry.ProjectId,
            BaseRevision = entry.BaseRevision,
            CreatedAtUtc = DateTime.SpecifyKind(entry.CreatedAtUtc, DateTimeKind.Utc),
            Author = entry.Author,
            PlanFingerprint = entry.PlanFingerprint,
            IsValid = entry.IsValid,
            ClipCount = entry.ClipCount,
            TimelineDurationFrames = entry.TimelineDurationFrames,
            RelativeJsonPath = entry.RelativeJsonPath,
            InputsFingerprint = entry.InputsFingerprint,
        };

        try
        {
            await _db.PlanRevisions.AddAsync(record, ct);
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Mesmo padrão de MediaImport/PipelineJobManager: não reenviar entidades não gravadas.
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    private static PlanRevisionIndexEntry ToEntry(PlanRevisionRecord r) => new(
        ProjectId: r.ProjectId,
        PlanId: r.PlanId,
        Revision: r.Revision,
        BaseRevision: r.BaseRevision,
        CreatedAtUtc: DateTime.SpecifyKind(r.CreatedAtUtc, DateTimeKind.Utc),
        Author: r.Author,
        PlanFingerprint: r.PlanFingerprint,
        IsValid: r.IsValid,
        ClipCount: r.ClipCount,
        TimelineDurationFrames: r.TimelineDurationFrames,
        RelativeJsonPath: r.RelativeJsonPath,
        InputsFingerprint: r.InputsFingerprint);
}
