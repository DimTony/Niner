using Core.Entities;
using Core.Enums;
using Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class JobRepository : IJobRepository
{
    private readonly AppDbContext _db;

    public JobRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Job> Create(Job job, CancellationToken ct = default)
    {
        job.CreatedAt = DateTimeOffset.UtcNow;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync(ct);
        return job;
    }

    public async Task Update(Job job, CancellationToken ct = default)
    {
        _db.Jobs.Update(job);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SoftDelete(Guid id, CancellationToken ct = default)
    {
        var job = await _db.Jobs.FindAsync([id], ct);
        if (job is null) return;
        job.DeletedAt = DateTimeOffset.UtcNow;
        job.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Job?> GetById(Guid id, CancellationToken ct = default)
    {
        return await _db.Jobs
            .Where(j => j.Id == id && j.DeletedAt == null)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Job?> GetByIdWithDependencies(Guid id, CancellationToken ct = default)
    {
        return await _db.Jobs
            .Include(j => j.Dependencies)
                .ThenInclude(d => d.DependsOn)
            .Where(j => j.Id == id && j.DeletedAt == null)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Job>> GetStaleLockJobs(
        DateTimeOffset lockedBefore,
        CancellationToken ct = default)
    {
        return await _db.Jobs
            .Where(j =>
                j.DeletedAt == null &&
                j.Status == JobStatus.Processing &&
                j.LockedAt != null &&
                j.LockedAt < lockedBefore)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Job>> GetDueScheduledJobs(
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        return await _db.Jobs
            .Where(j =>
                j.DeletedAt == null &&
                j.Status == JobStatus.Pending &&
                j.ScheduledAt <= asOf)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Job>> GetUnblockedDependents(
        Guid completedJobId,
        CancellationToken ct = default)
    {
        // Find all jobs that depend on completedJobId
        // where ALL of their dependencies are now completed
        var dependentJobIds = await _db.JobDependencies
            .Where(d => d.DependsOnId == completedJobId)
            .Select(d => d.JobId)
            .ToListAsync(ct);

        if (dependentJobIds.Count == 0)
            return [];

        var unblocked = new List<Job>();

        foreach (var jobId in dependentJobIds)
        {
            var hasBlockingDependency = await _db.JobDependencies
                .Where(d => d.JobId == jobId)
                .AnyAsync(d =>
                    d.DependsOn.Status != JobStatus.Completed &&
                    d.DependsOn.DeletedAt == null,
                    ct);

            if (!hasBlockingDependency)
            {
                var job = await _db.Jobs
                    .Where(j => j.Id == jobId &&
                                j.Status == JobStatus.Blocked &&
                                j.DeletedAt == null)
                    .FirstOrDefaultAsync(ct);

                if (job is not null)
                    unblocked.Add(job);
            }
        }

        return unblocked;
    }

    public async Task<IReadOnlyList<Job>> GetAll(
        JobStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.Jobs
            .Where(j => j.DeletedAt == null);

        if (status.HasValue)
            query = query.Where(j => j.Status == status.Value);

        return await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<Dictionary<JobStatus, int>> GetStatusCounts(
        CancellationToken ct = default)
    {
        var counts = await _db.Jobs
            .Where(j => j.DeletedAt == null)
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Ensure all statuses are present even if count is zero
        var result = Enum.GetValues<JobStatus>()
            .ToDictionary(s => s, _ => 0);

        foreach (var item in counts)
            result[item.Status] = item.Count;

        return result;
    }
}