using Microsoft.EntityFrameworkCore;
using TravelCut.Application.Ports;
using TravelCut.Application.Services;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Entities.Editorial;
using TravelCut.Infrastructure.Persistence;

namespace TravelCut.Infrastructure.Repositories;

public class SqliteProjectRepository : IProjectRepository
{
    private readonly TravelCutDbContext _db;

    public SqliteProjectRepository(TravelCutDbContext db)
    {
        _db = db;
    }

    public async Task<Project?> GetByIdAsync(Guid projectId, CancellationToken ct = default)
    {
        return await _db.Projects.FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
    }

    public async Task<Project?> GetByRootPathAsync(string rootPath, CancellationToken ct = default)
    {
        string normalized = NormalizeOrOriginal(rootPath);

        // Exato primeiro (registro já normalizado); depois caixa ignorada para aceitar registros antigos gravados com outra caixa.
        var exact = await _db.Projects.FirstOrDefaultAsync(p => p.RootPath == normalized, ct);
        if (exact != null)
        {
            return exact;
        }

        return await _db.Projects.FirstOrDefaultAsync(
            p => EF.Functions.Collate(p.RootPath, "NOCASE") == normalized, ct);
    }

    /// <summary>Raiz normalizada (absoluta, sem barra final). Entrada que o Path não aceita volta sem alteração (sem match).</summary>
    internal static string NormalizeOrOriginal(string rootPath)
    {
        try
        {
            return ProjectRootPathRules.Normalize(rootPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return rootPath;
        }
    }

    public async Task<List<Project>> ListRecentAsync(int limit = 10, CancellationToken ct = default)
    {
        return await _db.Projects
            .OrderByDescending(p => p.LastModifiedAtUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task SaveAsync(Project project, CancellationToken ct = default)
    {
        var existing = await _db.Projects.FirstOrDefaultAsync(p => p.ProjectId == project.ProjectId, ct);
        if (existing == null)
        {
            // Projeto novo: RootPath gravado já normalizado (sem barra final, caminho absoluto).
            project.RootPath = NormalizeOrOriginal(project.RootPath);
            await _db.Projects.AddAsync(project, ct);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(project);
        }
        await _db.SaveChangesAsync(ct);
    }
}

public class SqliteMediaRepository : IMediaRepository
{
    private readonly TravelCutDbContext _db;

    public SqliteMediaRepository(TravelCutDbContext db)
    {
        _db = db;
    }

    public async Task<MediaAsset?> GetByIdAsync(Guid mediaId, CancellationToken ct = default)
    {
        return await _db.MediaAssets
            .Include(m => m.Metadata)
            .FirstOrDefaultAsync(m => m.MediaId == mediaId, ct);
    }

    public async Task<List<MediaAsset>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        return await _db.MediaAssets
            .Include(m => m.Metadata)
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.RelativePath)
            .ToListAsync(ct);
    }

    public async Task SaveAsync(MediaAsset asset, CancellationToken ct = default)
    {
        var existing = await _db.MediaAssets
            .Include(m => m.Metadata)
            .FirstOrDefaultAsync(m => m.MediaId == asset.MediaId, ct);

        if (existing == null)
        {
            await _db.MediaAssets.AddAsync(asset, ct);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(asset);
            if (asset.Metadata != null)
            {
                if (existing.Metadata == null)
                {
                    existing.Metadata = asset.Metadata;
                }
                else
                {
                    _db.Entry(existing.Metadata).CurrentValues.SetValues(asset.Metadata);
                }
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveBatchAsync(IEnumerable<MediaAsset> assets, CancellationToken ct = default)
    {
        foreach (var asset in assets)
        {
            await SaveAsync(asset, ct);
        }
    }
}

public class SqliteTranscriptRepository : ITranscriptRepository
{
    private readonly TravelCutDbContext _db;

    public SqliteTranscriptRepository(TravelCutDbContext db)
    {
        _db = db;
    }

    public async Task<Transcript?> GetByMediaIdAsync(Guid mediaId, CancellationToken ct = default)
    {
        return await _db.Transcripts
            .Include(t => t.Words)
            .FirstOrDefaultAsync(t => t.MediaId == mediaId, ct);
    }

    /// <summary>
    /// Uma transcrição ativa por mídia: a nova substitui qualquer transcrição anterior da mesma mídia
    /// (ou com o mesmo Id). Remoção e inserção são feitas em duas gravações para evitar conflito de chave.
    /// Chamar dentro de uma transação do DbContext para atomicidade.
    /// </summary>
    public async Task SaveAsync(Transcript transcript, CancellationToken ct = default)
    {
        var previous = await _db.Transcripts
            .Include(t => t.Words)
            .Where(t => t.MediaId == transcript.MediaId || t.TranscriptId == transcript.TranscriptId)
            .ToListAsync(ct);

        if (previous.Count > 0)
        {
            foreach (var old in previous)
            {
                _db.TranscriptWords.RemoveRange(old.Words);
                _db.Transcripts.Remove(old);
            }
            await _db.SaveChangesAsync(ct);
        }

        await _db.Transcripts.AddAsync(transcript, ct);
        await _db.SaveChangesAsync(ct);
    }
}

public class SqliteEditPlanRepository : IEditPlanRepository
{
    private readonly TravelCutDbContext _db;

    public SqliteEditPlanRepository(TravelCutDbContext db)
    {
        _db = db;
    }

    public async Task<EditPlan?> GetLatestByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        return await _db.EditPlans
            .Include(p => p.Clips)
            .Where(p => p.ProjectId == projectId)
            .OrderByDescending(p => p.Revision)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SavePlanAsync(EditPlan plan, CancellationToken ct = default)
    {
        var existing = await _db.EditPlans
            .Include(p => p.Clips)
            .FirstOrDefaultAsync(p => p.PlanId == plan.PlanId, ct);

        if (existing == null)
        {
            await _db.EditPlans.AddAsync(plan, ct);
        }
        else
        {
            _db.Entry(existing).CurrentValues.SetValues(plan);
            existing.Clips = plan.Clips;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<EditSuggestion>> ListSuggestionsByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var mediaIds = await _db.MediaAssets
            .Where(m => m.ProjectId == projectId)
            .Select(m => m.MediaId)
            .ToListAsync(ct);

        return await _db.EditSuggestions
            .Where(s => mediaIds.Contains(s.MediaId))
            .OrderBy(s => s.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task SaveSuggestionsBatchAsync(IEnumerable<EditSuggestion> suggestions, CancellationToken ct = default)
    {
        foreach (var s in suggestions)
        {
            var existing = await _db.EditSuggestions.FirstOrDefaultAsync(x => x.SuggestionId == s.SuggestionId, ct);
            if (existing == null)
            {
                await _db.EditSuggestions.AddAsync(s, ct);
            }
            else
            {
                _db.Entry(existing).CurrentValues.SetValues(s);
            }
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Inserção append-only. A Version é calculada DENTRO da transação (última + 1) e o índice único
    /// (SuggestionId, Version) impede duplicata; em colisão concorrente tenta de novo algumas vezes.
    /// </summary>
    public async Task SaveDecisionAsync(EditDecision decision, CancellationToken ct = default)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var last = await _db.EditDecisions
                    .Where(d => d.SuggestionId == decision.SuggestionId)
                    .Select(d => (int?)d.Version)
                    .MaxAsync(ct);
                decision.Version = (last ?? 0) + 1;

                await _db.EditDecisions.AddAsync(decision, ct);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return;
            }
            catch (DbUpdateException) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                _db.ChangeTracker.Clear();
            }
            catch
            {
                _db.ChangeTracker.Clear();
                throw;
            }
        }
    }

    public async Task<List<EditDecision>> ListDecisionsByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var mediaIds = await _db.MediaAssets
            .Where(m => m.ProjectId == projectId)
            .Select(m => m.MediaId)
            .ToListAsync(ct);

        var suggestionIds = await _db.EditSuggestions
            .Where(s => mediaIds.Contains(s.MediaId))
            .Select(s => s.SuggestionId)
            .ToListAsync(ct);

        return await _db.EditDecisions
            .AsNoTracking()
            .Where(d => suggestionIds.Contains(d.SuggestionId))
            .OrderBy(d => d.TimestampUtc)
            .ThenBy(d => d.Version)
            .ToListAsync(ct);
    }

    /// <summary>Última decisão pela Version (não pelo timestamp).</summary>
    public async Task<EditDecision?> GetLatestDecisionBySuggestionAsync(Guid suggestionId, CancellationToken ct = default)
    {
        return await _db.EditDecisions
            .AsNoTracking()
            .Where(d => d.SuggestionId == suggestionId)
            .OrderByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<EditDecision>> ListDecisionsBySuggestionAsync(Guid suggestionId, CancellationToken ct = default)
    {
        return await _db.EditDecisions
            .AsNoTracking()
            .Where(d => d.SuggestionId == suggestionId)
            .OrderBy(d => d.Version)
            .ToListAsync(ct);
    }

    public async Task<List<EditSuggestion>> ListSuggestionsByIdsAsync(IReadOnlyCollection<Guid> suggestionIds, CancellationToken ct = default)
    {
        if (suggestionIds.Count == 0) return new List<EditSuggestion>();
        var wanted = suggestionIds.Distinct().ToList();
        return await _db.EditSuggestions
            .AsNoTracking()
            .Where(s => wanted.Contains(s.SuggestionId))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Lote em uma única transação: Version = última + 1 por sugestão, calculada dentro da transação. Se qualquer gravação
    /// falhar, a transação é desfeita (nenhuma decisão do lote fica gravada). Reexecuta só em colisão de Version
    /// (SQLITE_CONSTRAINT_UNIQUE); outras falhas são relançadas de imediato.
    /// </summary>
    public async Task SaveDecisionsAtomicAsync(IReadOnlyList<EditDecision> decisions, CancellationToken ct = default)
    {
        if (decisions.Count == 0) return;

        const int maxAttempts = 3;
        const int sqliteUniqueViolation = 2067; // SQLITE_CONSTRAINT_UNIQUE
        for (int attempt = 1; ; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                foreach (var decision in decisions)
                {
                    var last = await _db.EditDecisions
                        .Where(d => d.SuggestionId == decision.SuggestionId)
                        .Select(d => (int?)d.Version)
                        .MaxAsync(ct);
                    decision.Version = (last ?? 0) + 1;
                    await _db.EditDecisions.AddAsync(decision, ct);
                    await _db.SaveChangesAsync(ct);
                }
                await tx.CommitAsync(ct);
                return;
            }
            catch (DbUpdateException ex) when (attempt < maxAttempts && !ct.IsCancellationRequested && IsUniqueViolation(ex, sqliteUniqueViolation))
            {
                _db.ChangeTracker.Clear();
            }
            catch
            {
                _db.ChangeTracker.Clear();
                throw;
            }
        }
    }

    /// <summary>
    /// Decisão condicional: histórico lido e gravação na mesma transação. Em colisão de Version (outro gravador venceu),
    /// a transação é refeita e <paramref name="build"/> roda de novo com o histórico atualizado (até 3 tentativas).
    /// </summary>
    public async Task<EditDecision?> SaveDecisionIfAsync(
        Guid suggestionId,
        Func<IReadOnlyList<EditDecision>, EditDecision?> build,
        CancellationToken ct = default)
    {
        const int maxAttempts = 3;
        const int sqliteUniqueViolation = 2067; // SQLITE_CONSTRAINT_UNIQUE
        for (int attempt = 1; ; attempt++)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var history = await _db.EditDecisions
                    .AsNoTracking()
                    .Where(d => d.SuggestionId == suggestionId)
                    .OrderBy(d => d.Version)
                    .ToListAsync(ct);

                var decision = build(history);
                if (decision is null)
                {
                    await tx.RollbackAsync(ct);
                    return null;
                }

                int last = history.Count == 0 ? 0 : history.Max(d => d.Version);
                decision.SuggestionId = suggestionId;
                decision.Version = last + 1;
                await _db.EditDecisions.AddAsync(decision, ct);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return decision;
            }
            catch (DbUpdateException ex) when (attempt < maxAttempts && !ct.IsCancellationRequested && IsUniqueViolation(ex, sqliteUniqueViolation))
            {
                _db.ChangeTracker.Clear();
            }
            catch
            {
                _db.ChangeTracker.Clear();
                throw;
            }
        }
    }

    private static bool IsUniqueViolation(Exception ex, int extendedCode)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is Microsoft.Data.Sqlite.SqliteException sqlite && sqlite.SqliteExtendedErrorCode == extendedCode)
                return true;
        }
        return false;
    }

    public async Task<List<Guid>> FindExistingSuggestionIdsAsync(IReadOnlyCollection<Guid> suggestionIds, CancellationToken ct = default)
    {
        if (suggestionIds.Count == 0) return new List<Guid>();
        var wanted = suggestionIds.Distinct().ToList();
        return await _db.EditSuggestions
            .AsNoTracking()
            .Where(s => wanted.Contains(s.SuggestionId))
            .Select(s => s.SuggestionId)
            .Distinct()
            .ToListAsync(ct);
    }
}

public class SqliteSceneClassificationRepository : ISceneClassificationRepository
{
    private readonly TravelCutDbContext _db;

    public SqliteSceneClassificationRepository(TravelCutDbContext db)
    {
        _db = db;
    }

    public async Task<SceneClassificationEntry?> GetByIdAsync(Guid entryId, CancellationToken ct = default)
    {
        return await _db.SceneClassifications
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.EntryId == entryId, ct);
    }

    public async Task<List<SceneClassificationEntry>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        return await _db.SceneClassifications
            .AsNoTracking()
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.MediaId)
            .ThenBy(s => s.StartSeconds)
            .ToListAsync(ct);
    }

    public async Task<List<SceneClassificationEntry>> ListByMediaAsync(Guid mediaId, CancellationToken ct = default)
    {
        return await _db.SceneClassifications
            .AsNoTracking()
            .Where(s => s.MediaId == mediaId)
            .OrderBy(s => s.StartSeconds)
            .ToListAsync(ct);
    }

    public async Task SaveAsync(SceneClassificationEntry entry, CancellationToken ct = default)
    {
        var existing = await _db.SceneClassifications
            .FirstOrDefaultAsync(s => s.EntryId == entry.EntryId, ct);

        if (existing is null)
        {
            await _db.SceneClassifications.AddAsync(entry, ct);
        }
        else
        {
            existing.StartSeconds = entry.StartSeconds;
            existing.EndSeconds = entry.EndSeconds;
            existing.IsFullVideo = entry.IsFullVideo;
            existing.Environment = entry.Environment;
            existing.Interaction = entry.Interaction;
            existing.Framing = entry.Framing;
            existing.Movement = entry.Movement;
            existing.EditorialProfile = entry.EditorialProfile;
            existing.Notes = entry.Notes;
            existing.UpdatedAtUtc = entry.UpdatedAtUtc;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveBatchAsync(IEnumerable<SceneClassificationEntry> entries, CancellationToken ct = default)
    {
        foreach (var entry in entries)
        {
            await SaveAsync(entry, ct);
        }
    }

    public async Task DeleteAsync(Guid entryId, CancellationToken ct = default)
    {
        var existing = await _db.SceneClassifications
            .FirstOrDefaultAsync(s => s.EntryId == entryId, ct);

        if (existing is not null)
        {
            _db.SceneClassifications.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
    }
}

public class SqliteEditorialTransitionRepository : IEditorialTransitionRepository
{
    private readonly TravelCutDbContext _db;

    public SqliteEditorialTransitionRepository(TravelCutDbContext db)
    {
        _db = db;
    }

    public async Task<EditorialTransition?> GetByIdAsync(Guid transitionId, CancellationToken ct = default)
    {
        return await _db.EditorialTransitions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TransitionId == transitionId, ct);
    }

    public async Task<List<EditorialTransition>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        return await _db.EditorialTransitions
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<List<EditorialTransition>> ListByMediaPairAsync(Guid mediaIdA, Guid mediaIdB, CancellationToken ct = default)
    {
        return await _db.EditorialTransitions
            .AsNoTracking()
            .Where(t => t.MediaIdA == mediaIdA && t.MediaIdB == mediaIdB)
            .ToListAsync(ct);
    }

    public async Task SaveAsync(EditorialTransition transition, CancellationToken ct = default)
    {
        var existing = await _db.EditorialTransitions
            .FirstOrDefaultAsync(t => t.TransitionId == transition.TransitionId, ct);

        if (existing is null)
        {
            await _db.EditorialTransitions.AddAsync(transition, ct);
        }
        else
        {
            existing.Type = transition.Type;
            existing.DurationSeconds = transition.DurationSeconds;
            existing.Preset = transition.Preset;
            existing.Status = transition.Status;
            existing.Origin = transition.Origin;
            existing.Rationale = transition.Rationale;
            existing.IsAdjacentOnTimeline = transition.IsAdjacentOnTimeline;
            existing.InsufficientHandles = transition.InsufficientHandles;
            existing.HandleWarningMessage = transition.HandleWarningMessage;
            existing.AvailableHandleFramesA = transition.AvailableHandleFramesA;
            existing.AvailableHandleFramesB = transition.AvailableHandleFramesB;
            existing.TimelineClipIdA = transition.TimelineClipIdA;
            existing.TimelineClipIdB = transition.TimelineClipIdB;
            existing.UpdatedAtUtc = transition.UpdatedAtUtc;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveBatchAsync(IEnumerable<EditorialTransition> transitions, CancellationToken ct = default)
    {
        foreach (var transition in transitions)
        {
            await SaveAsync(transition, ct);
        }
    }

    public async Task DeleteAsync(Guid transitionId, CancellationToken ct = default)
    {
        var existing = await _db.EditorialTransitions
            .FirstOrDefaultAsync(t => t.TransitionId == transitionId, ct);

        if (existing is not null)
        {
            _db.EditorialTransitions.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }
    }
}
