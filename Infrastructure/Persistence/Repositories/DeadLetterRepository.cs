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