using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using TravelCut.Application.Ports;
using TravelCut.Domain.Entities;
using TravelCut.Domain.Enums;
using TravelCut.Infrastructure.Persistence;

namespace TravelCut.Infrastructure.Jobs;

public class InMemoryJobQueue : IJobQueue
{
    private readonly TravelCutDbContext _db;
    private readonly ConcurrentDictionary<Guid, ProcessingJob> _activeJobs = new();

    public InMemoryJobQueue(TravelCutDbContext db)
    {
        _db = db;
    }

    public async Task<ProcessingJob> EnqueueAsync(Guid projectId, JobType type, CancellationToken ct = default)
    {
        var job = new ProcessingJob
        {
            JobId = Guid.NewGuid(),
            ProjectId = projectId,
            Type = type,
            Status = JobStatus.Queued,
            CompletedUnits = 0,
            TotalUnits = 100,
            CurrentStepDescription = "Na fila de processamento",
            CreatedAtUtc = DateTime.UtcNow
        };

        _activeJobs[job.JobId] = job;
        await _db.ProcessingJobs.AddAsync(job, ct);
        await _db.SaveChangesAsync(ct);

        return job;
    }

    public async Task UpdateProgressAsync(Guid jobId, int completed, int total, string stepDescription, CancellationToken ct = default)
    {
        if (_activeJobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Running;
            job.CompletedUnits = completed;
            job.TotalUnits = total;
            job.CurrentStepDescription = stepDescription;
            job.StartedAtUtc ??= DateTime.UtcNow;
        }

        var dbJob = await _db.ProcessingJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (dbJob != null)
        {
            dbJob.Status = JobStatus.Running;
            dbJob.CompletedUnits = completed;
            dbJob.TotalUnits = total;
            dbJob.CurrentStepDescription = stepDescription;
            dbJob.StartedAtUtc ??= DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task MarkCompletedAsync(Guid jobId, CancellationToken ct = default)
    {
        if (_activeJobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Completed;
            job.CompletedUnits = job.TotalUnits;
            job.CurrentStepDescription = "Concluído com sucesso";
            job.FinishedAtUtc = DateTime.UtcNow;
        }

        var dbJob = await _db.ProcessingJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (dbJob != null)
        {
            dbJob.Status = JobStatus.Completed;
            dbJob.CompletedUnits = dbJob.TotalUnits;
            dbJob.CurrentStepDescription = "Concluído com sucesso";
            dbJob.FinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task MarkFailedAsync(Guid jobId, string errorMessage, CancellationToken ct = default)
    {
        if (_activeJobs.TryGetValue(jobId, out var job))
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = errorMessage;
            job.CurrentStepDescription = "Falha no processamento";
            job.FinishedAtUtc = DateTime.UtcNow;
        }

        var dbJob = await _db.ProcessingJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
        if (dbJob != null)
        {
            dbJob.Status = JobStatus.Failed;
            dbJob.ErrorMessage = errorMessage;
            dbJob.CurrentStepDescription = "Falha no processamento";
            dbJob.FinishedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<ProcessingJob?> GetJobAsync(Guid jobId, CancellationToken ct = default)
    {
        if (_activeJobs.TryGetValue(jobId, out var job))
        {
            return job;
        }

        return await _db.ProcessingJobs.FirstOrDefaultAsync(j => j.JobId == jobId, ct);
    }

    public async Task<List<ProcessingJob>> ListJobsByProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        return await _db.ProcessingJobs
            .Where(j => j.ProjectId == projectId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .ToListAsync(ct);
    }
}
