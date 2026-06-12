using Core.Entities;
using Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class DeadLetterRepository : IDeadLetterRepository
{
    private readonly AppDbContext _db;

    public DeadLetterRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<DeadLetterEntry> Create(
        DeadLetterEntry entry,
        CancellationToken ct = default)
    {
        entry.CreatedAt = DateTimeOffset.UtcNow;
        _db.DeadLetterEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task<DeadLetterEntry> Upsert(
        Guid jobId,
        string errorDetails,
        int failureCount,
        CancellationToken ct = default)
    {
        var existing = await _db.DeadLetterEntries
            .FirstOrDefaultAsync(d => d.JobId == jobId, ct);

        if (existing is not null)
        {
            existing.ErrorDetails = errorDetails;
            existing.FailureCount = failureCount;
            existing.Resolved     = false;
            existing.ResolvedAt   = null;
            existing.CreatedAt    = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return existing;
        }

        var entry = new DeadLetterEntry
        {
            Id           = Guid.NewGuid(),
            JobId        = jobId,
            ErrorDetails = errorDetails,
            FailureCount = failureCount,
            CreatedAt    = DateTimeOffset.UtcNow,
            Resolved     = false
        };

        _db.DeadLetterEntries.Add(entry);
        await _db.SaveChangesAsync(ct);
        return entry;
    }


    public async Task<IReadOnlyList<DeadLetterEntry>> GetUnresolved(
        CancellationToken ct = default)
    {
        return await _db.DeadLetterEntries
            .Include(d => d.Job)
            .Where(d => !d.Resolved)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<DeadLetterEntry?> GetByJobId(
        Guid jobId,
        CancellationToken ct = default)
    {
        return await _db.DeadLetterEntries
            .Include(d => d.Job)
            .Where(d => d.JobId == jobId)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task MarkResolved(Guid id, CancellationToken ct = default)
    {
        var entry = await _db.DeadLetterEntries.FindAsync([id], ct);
        if (entry is null) return;
        entry.Resolved = true;
        entry.ResolvedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> GetUnresolvedCount(CancellationToken ct = default)
    {
        return await _db.DeadLetterEntries
            .CountAsync(d => !d.Resolved, ct);
    }
}