using System.Text.Json;
using Core.Entities;
using Core.Enums;
using Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public class JobLogRepository : IJobLogRepository
{
    private readonly AppDbContext _db;

    public JobLogRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task Create(
        Guid jobId,
        LogEvent logEvent,
        string message,
        object? metadata = null,
        CancellationToken ct = default)
    {
        var log = new JobLog
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Event = logEvent,
            Message = message,
            Metadata = metadata is not null
                ? JsonSerializer.Serialize(metadata)
                : null,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.JobLogs.Add(log);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<JobLog>> GetByJobId(
        Guid jobId,
        CancellationToken ct = default)
    {
        return await _db.JobLogs
            .Where(l => l.JobId == jobId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);
    }
}