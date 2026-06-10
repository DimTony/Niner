namespace Core.Interfaces;

public interface IJobQueueService
{
    // Ready queue (heap / sorted set)
    Task EnqueueReady(Guid jobId, double score, CancellationToken ct = default);
    Task<Guid?> DequeueNext(CancellationToken ct = default);
    Task RemoveFromReady(Guid jobId, CancellationToken ct = default);
    Task UpdateScore(Guid jobId, double newScore, CancellationToken ct = default);

    // Scheduled set
    Task EnqueueScheduled(Guid jobId, DateTimeOffset scheduledAt, CancellationToken ct = default);
    Task RemoveFromScheduled(Guid jobId, CancellationToken ct = default);
    Task<IReadOnlyList<(Guid JobId, double Score)>> GetDueScheduledJobs(
        DateTimeOffset asOf,
        CancellationToken ct = default);

    // Timing wheel
    Task AddToWheelSlot(Guid jobId, int slot, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> PopWheelSlot(int slot, CancellationToken ct = default);
    Task<int> GetWheelPointer(CancellationToken ct = default);
    Task SetWheelPointer(int slot, CancellationToken ct = default);

    // Locking
    Task<bool> AcquireLock(Guid jobId, string workerId, TimeSpan ttl, CancellationToken ct = default);
    Task<bool> RenewLock(Guid jobId, string workerId, TimeSpan ttl, CancellationToken ct = default);
    Task ReleaseLock(Guid jobId, string workerId, CancellationToken ct = default);

    // DLQ counter
    Task<long> IncrementDlqCount(CancellationToken ct = default);
    Task<long> DecrementDlqCount(CancellationToken ct = default);
    Task ResetDlqCount(CancellationToken ct = default);

    // Worker heartbeat
    Task SetWorkerHeartbeat(string workerId, TimeSpan ttl, CancellationToken ct = default);
}