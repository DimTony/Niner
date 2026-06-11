using Core.Interfaces;
using StackExchange.Redis;

namespace Infrastructure.Redis;

public class JobQueueService : IJobQueueService
{
    private readonly IDatabase _db;

    private const string ReadyQueue      = "scheduler:ready_queue";
    private const string ScheduledJobs   = "scheduler:scheduled_jobs";
    private const string WheelPointer    = "scheduler:wheel:pointer";
    private const string DlqCount        = "dlq:count";

    private static string WheelSlot(int slot) => $"scheduler:wheel:slot:{slot}";
    private static string JobLock(Guid jobId)  => $"lock:job:{jobId}";
    private static string WorkerHb(string id)  => $"worker:heartbeat:{id}";

    public JobQueueService(IConnectionMultiplexer redis)
    {
        _db = redis.GetDatabase();
    }

    // -------------------------
    // Ready Queue
    // -------------------------

    public async Task EnqueueReady(
        Guid jobId,
        double score,
        CancellationToken ct = default)
    {
        await _db.SortedSetAddAsync(ReadyQueue, jobId.ToString(), score);
    }

    public async Task<bool> EnqueueReadyIfAbsent(
        Guid jobId,
        double score,
        CancellationToken ct = default)
    {
        // NX = only add if member does not exist — idempotent
        return await _db.SortedSetAddAsync(
            ReadyQueue,
            jobId.ToString(),
            score,
            SortedSetWhen.NotExists);
    }

    public async Task<Guid?> DequeueNext(CancellationToken ct = default)
    {
        // ZPOPMIN is atomic — only one worker gets each job
        var result = await _db.SortedSetPopAsync(ReadyQueue, Order.Ascending);
        if (result is null) return null;
        return Guid.TryParse(result.Value.Element, out var id) ? id : null;
    }

    public async Task RemoveFromReady(Guid jobId, CancellationToken ct = default)
    {
        await _db.SortedSetRemoveAsync(ReadyQueue, jobId.ToString());
    }

    public async Task UpdateScore(
        Guid jobId,
        double newScore,
        CancellationToken ct = default)
    {
        // XX = only update, don't add if missing
        await _db.SortedSetAddAsync(
            ReadyQueue,
            jobId.ToString(),
            newScore,
            SortedSetWhen.Exists);
    }

    // -------------------------
    // Scheduled Set
    // -------------------------

    public async Task EnqueueScheduled(
        Guid jobId,
        DateTimeOffset scheduledAt,
        CancellationToken ct = default)
    {
        var score = scheduledAt.ToUnixTimeMilliseconds();
        await _db.SortedSetAddAsync(ScheduledJobs, jobId.ToString(), score);
    }

    public async Task RemoveFromScheduled(
        Guid jobId,
        CancellationToken ct = default)
    {
        await _db.SortedSetRemoveAsync(ScheduledJobs, jobId.ToString());
    }

    public async Task<IReadOnlyList<(Guid JobId, double Score)>> GetDueScheduledJobs(
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        var upperBound = (double)asOf.ToUnixTimeMilliseconds();

        var entries = await _db.SortedSetRangeByScoreWithScoresAsync(
            ScheduledJobs,
            start: double.NegativeInfinity,
            stop: upperBound);

        return entries
            .Select(e => (
                JobId: Guid.Parse(e.Element!),
                Score: e.Score))
            .ToList();
    }

    // -------------------------
    // Timing Wheel
    // -------------------------

    public async Task AddToWheelSlot(
        Guid jobId,
        int slot,
        CancellationToken ct = default)
    {
        await _db.SetAddAsync(WheelSlot(slot), jobId.ToString());
    }

    public async Task<IReadOnlyList<Guid>> PopWheelSlot(
        int slot,
        CancellationToken ct = default)
    {
        var key = WheelSlot(slot);

        // Get all members then delete the key atomically via Lua
        var script = @"
            local members = redis.call('SMEMBERS', KEYS[1])
            redis.call('DEL', KEYS[1])
            return members";

        var result = await _db.ScriptEvaluateAsync(
            script,
            keys: [new RedisKey(key)]);

        if (result.IsNull) return [];

        return ((RedisResult[])result!)
            .Select(r => Guid.Parse(r.ToString()))
            .ToList();
    }

    public async Task<int> GetWheelPointer(CancellationToken ct = default)
    {
        var val = await _db.StringGetAsync(WheelPointer);
        return val.HasValue ? (int)val : 0;
    }

    public async Task SetWheelPointer(int slot, CancellationToken ct = default)
    {
        await _db.StringSetAsync(WheelPointer, slot);
    }

    // -------------------------
    // Locking
    // -------------------------

    public async Task<bool> AcquireLock(
        Guid jobId,
        string workerId,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        // SET NX — only succeeds if key does not exist
        return await _db.StringSetAsync(
            JobLock(jobId),
            workerId,
            ttl,
            When.NotExists);
    }

    public async Task<bool> RenewLock(
        Guid jobId,
        string workerId,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        // Only renew if this worker still owns the lock
        var script = @"
            if redis.call('GET', KEYS[1]) == ARGV[1] then
                return redis.call('PEXPIRE', KEYS[1], ARGV[2])
            else
                return 0
            end";

        var result = await _db.ScriptEvaluateAsync(
            script,
            keys: [new RedisKey(JobLock(jobId))],
            values: [workerId, (long)ttl.TotalMilliseconds]);

        return (long)result == 1;
    }

    public async Task ReleaseLock(
        Guid jobId,
        string workerId,
        CancellationToken ct = default)
    {
        // Only release if this worker owns the lock
        var script = @"
            if redis.call('GET', KEYS[1]) == ARGV[1] then
                return redis.call('DEL', KEYS[1])
            else
                return 0
            end";

        await _db.ScriptEvaluateAsync(
            script,
            keys: [new RedisKey(JobLock(jobId))],
            values: [workerId]);
    }

    // -------------------------
    // DLQ Counter
    // -------------------------

    public async Task<long> IncrementDlqCount(CancellationToken ct = default)
        => await _db.StringIncrementAsync(DlqCount);

    public async Task<long> DecrementDlqCount(CancellationToken ct = default)
        => await _db.StringDecrementAsync(DlqCount);

    public async Task ResetDlqCount(CancellationToken ct = default)
        => await _db.StringSetAsync(DlqCount, 0);

    // -------------------------
    // Worker Heartbeat
    // -------------------------

    public async Task SetWorkerHeartbeat(
        string workerId,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        await _db.StringSetAsync(
            WorkerHb(workerId),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ttl);
    }
}