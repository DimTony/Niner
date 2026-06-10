using Core.Entities;
using Core.Enums;

namespace Core.Interfaces;

public interface IJobRepository
{
    Task<Job> Create(Job job, CancellationToken ct = default);
    Task Update(Job job, CancellationToken ct = default);
    Task SoftDelete(Guid id, CancellationToken ct = default);
    Task<Job?> GetById(Guid id, CancellationToken ct = default);
    Task<Job?> GetByIdWithDependencies(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Job>> GetStaleLockJobs(
        DateTimeOffset lockedBefore,
        CancellationToken ct = default);
    Task<IReadOnlyList<Job>> GetDueScheduledJobs(
        DateTimeOffset asOf,
        CancellationToken ct = default);
    Task<IReadOnlyList<Job>> GetUnblockedDependents(
        Guid completedJobId,
        CancellationToken ct = default);
    Task<IReadOnlyList<Job>> GetAll(
        JobStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default);
    Task<Dictionary<JobStatus, int>> GetStatusCounts(
        CancellationToken ct = default);
}
public interface IDeadLetterRepository
{
    Task<DeadLetterEntry> Create(DeadLetterEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<DeadLetterEntry>> GetUnresolved(CancellationToken ct = default);
    Task<DeadLetterEntry?> GetByJobId(Guid jobId, CancellationToken ct = default);
    Task MarkResolved(Guid id, CancellationToken ct = default);
    Task<int> GetUnresolvedCount(CancellationToken ct = default);
}
public interface IJobLogRepository
{
    Task Create(
        Guid jobId,
        LogEvent logEvent,
        string message,
        object? metadata = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<JobLog>> GetByJobId(
        Guid jobId,
        CancellationToken ct = default);
}