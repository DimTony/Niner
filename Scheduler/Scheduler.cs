using Core.Enums;
using Core.Interfaces;
using Core.Options;
using Core.Scheduling;
using Core.Services;
using Microsoft.Extensions.Options;

namespace Scheduler;

public class SchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobQueueService     _queue;
    private readonly SchedulerOptions     _options;
    private readonly ILogger<SchedulerService> _logger;

    // In-memory structures
    private readonly JobHeap     _heap;
    private readonly TimingWheel _wheel;

    public SchedulerService(
        IServiceScopeFactory scopeFactory,
        IJobQueueService queue,
        IOptions<SchedulerOptions> options,
        ILogger<SchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _queue        = queue;
        _options      = options.Value;
        _logger       = logger;
        _heap         = new JobHeap();
        _wheel        = new TimingWheel(_options.WheelSizeSlots);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Scheduler started.");

        // Restore state from Redis/Postgres on startup
        await RestoreStateAsync(ct);

        // Run all loops concurrently
        await Task.WhenAll(
            RunPromotionLoop(ct),
            RunWheelLoop(ct),
            RunAgingLoop(ct),
            RunOrphanRecoveryLoop(ct),
            RunBenchmarkOnce(ct));
    }

    // --------------------------------------------------
    // Startup — rebuild in-memory structures from Redis
    // --------------------------------------------------

    private async Task RestoreStateAsync(CancellationToken ct)
    {
        _logger.LogInformation("Restoring scheduler state from Redis.");

        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        // Sync wheel pointer from Redis
        var pointer = await _queue.GetWheelPointer(ct);
        for (var i = 0; i < pointer; i++)
            _wheel.Tick(); // advance to stored position

        // Reload pending jobs into heap
        var pending = await jobRepo.GetDueScheduledJobs(
            DateTimeOffset.UtcNow.AddHours(1), ct);

        foreach (var job in pending)
        {
            var score = JobScoreCalculator.Calculate(
                job.Priority, job.ScheduledAt, job.CreatedAt);
            _heap.Push(new HeapEntry(job.Id, score));
            _wheel.AddJob(job.Id, job.ScheduledAt);
        }

        _logger.LogInformation(
            "State restored. HeapSize={HeapSize}", _heap.Count);
    }

    // --------------------------------------------------
    // Promotion loop — heap / sorted set
    // --------------------------------------------------

    private async Task RunPromotionLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await PromoteDueJobsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Promotion loop error.");
            }

            await Task.Delay(_options.PromotionIntervalMs, ct);
        }
    }

    private async Task PromoteDueJobsAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Pull due jobs from Redis scheduled set
        var due = await _queue.GetDueScheduledJobs(now, ct);
        if (due.Count == 0) return;

        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        foreach (var (jobId, _) in due)
        {
            var job = await jobRepo.GetById(jobId, ct);
            if (job is null) continue;

            // Skip cancelled or already promoted jobs
            if (job.Status is JobStatus.Cancelled or
                              JobStatus.Completed or
                              JobStatus.Processing)
            {
                await _queue.RemoveFromScheduled(jobId, ct);
                continue;
            }

            var score = JobScoreCalculator.Calculate(
                job.Priority, job.ScheduledAt, job.CreatedAt);

            // Move from scheduled set → ready queue
            await _queue.RemoveFromScheduled(jobId, ct);
            await _queue.EnqueueReady(jobId, score, ct);

            // Mirror in heap
            _heap.Push(new HeapEntry(jobId, score));

            _logger.LogInformation(
                "Job promoted to ready queue. JobId={JobId} Score={Score:F0}",
                jobId, score);
        }
    }

    // --------------------------------------------------
    // Timing wheel loop
    // --------------------------------------------------

    private async Task RunWheelLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TickWheelAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Timing wheel error.");
            }

            await Task.Delay(_options.WheelTickIntervalMs, ct);
        }
    }

    private async Task TickWheelAsync(CancellationToken ct)
    {
        // Advance in-memory wheel
        var due = _wheel.Tick();

        // Persist pointer
        await _queue.SetWheelPointer(_wheel.CurrentSlot, ct);

        if (due.Count == 0) return;

        _logger.LogDebug(
            "Timing wheel tick. Slot={Slot} DueJobs={Count}",
            _wheel.CurrentSlot, due.Count);

        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        foreach (var jobId in due)
        {
            var job = await jobRepo.GetById(jobId, ct);
            if (job is null) continue;

            if (job.Status is JobStatus.Cancelled or
                              JobStatus.Completed or
                              JobStatus.Processing)
                continue;

            // Only promote if it's actually due
            // (wheel slots repeat every hour — check actual time)
            if (job.ScheduledAt > DateTimeOffset.UtcNow.AddSeconds(1))
                continue;

            var score = JobScoreCalculator.Calculate(
                job.Priority, job.ScheduledAt, job.CreatedAt);

            await _queue.EnqueueReady(jobId, score, ct);

            _logger.LogInformation(
                "Timing wheel promoted job. JobId={JobId} Slot={Slot}",
                jobId, _wheel.CurrentSlot);
        }
    }

    // --------------------------------------------------
    // Aging loop — starvation prevention
    // --------------------------------------------------

    private async Task RunAgingLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ApplyAgingAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Aging loop error.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.AgingIntervalSeconds), ct);
        }
    }

    private async Task ApplyAgingAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        // Get all pending jobs in the ready queue
        var pendingJobs = await jobRepo.GetDueScheduledJobs(
            DateTimeOffset.UtcNow, ct);

        if (pendingJobs.Count == 0) return;

        var now    = DateTimeOffset.UtcNow;
        var boosted = 0;

        foreach (var job in pendingJobs)
        {
            var effective = JobScoreCalculator.GetEffectivePriority(
                job.Priority, job.CreatedAt, now);

            // Only update if effective priority is better than actual
            if (effective >= job.Priority) continue;

            var newScore = JobScoreCalculator.Calculate(
                job.Priority, job.ScheduledAt, job.CreatedAt, effective);

            await _queue.UpdateScore(job.Id, newScore, ct);

            // Mirror in heap
            _heap.UpdateScore(job.Id, newScore);

            boosted++;

            _logger.LogInformation(
                "Job priority boosted by aging. JobId={JobId} " +
                "ActualPriority={Actual} EffectivePriority={Effective} NewScore={Score:F0}",
                job.Id, job.Priority, effective, newScore);
        }

        if (boosted > 0)
            _logger.LogInformation(
                "Aging pass complete. JobsBoosted={Count}", boosted);
    }

    // --------------------------------------------------
    // Benchmark — runs once on startup, logs results
    // --------------------------------------------------

    private async Task RunBenchmarkOnce(CancellationToken ct)
    {
        // Small delay so startup logs don't get buried
        await Task.Delay(TimeSpan.FromSeconds(5), ct);

        try
        {
            _logger.LogInformation(
                "Running scheduler benchmark. JobCount={Count}",
                _options.BenchmarkJobCount);

            var benchmark = new SchedulerBenchmark();
            var (heap, wheel) = benchmark.Run(_options.BenchmarkJobCount);

            _logger.LogInformation(
                "BENCHMARK [HeapResult] " +
                "Algorithm={Algorithm} " +
                "JobCount={JobCount} " +
                "InsertionMs={InsertionMs} " +
                "PromotionMs={PromotionMs} " +
                "TotalMs={TotalMs} " +
                "InsertionsPerSec={InsertionsPerSec:F0} " +
                "PromotionsPerSec={PromotionsPerSec:F0}",
                heap.Algorithm, heap.JobCount,
                heap.InsertionMs, heap.PromotionMs, heap.TotalMs,
                heap.InsertionsPerSecond, heap.PromotionsPerSecond);

            _logger.LogInformation(
                "BENCHMARK [WheelResult] " +
                "Algorithm={Algorithm} " +
                "JobCount={JobCount} " +
                "InsertionMs={InsertionMs} " +
                "PromotionMs={PromotionMs} " +
                "TotalMs={TotalMs} " +
                "InsertionsPerSec={InsertionsPerSec:F0} " +
                "PromotionsPerSec={PromotionsPerSec:F0}",
                wheel.Algorithm, wheel.JobCount,
                wheel.InsertionMs, wheel.PromotionMs, wheel.TotalMs,
                wheel.InsertionsPerSecond, wheel.PromotionsPerSecond);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Benchmark failed.");
        }
    }

    private async Task RunOrphanRecoveryLoop(CancellationToken ct)
    {
        // Wait on startup to let normal promotion run first
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RecoverOrphanedJobsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Orphan recovery error.");
            }

            // Run every 60 seconds — this is a safety net, not the primary path
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
        }
    }

    private async Task RecoverOrphanedJobsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();

        // Find all pending jobs whose scheduled_at has passed
        // These should be in Redis but may not be
        var due = await jobRepo.GetDueScheduledJobs(DateTimeOffset.UtcNow, ct);

        if (due.Count == 0) return;

        var recovered = 0;

        foreach (var job in due)
        {
            // Check if it is already in the ready queue
            // We do this by attempting ZADD NX — only adds if not present
            var score = JobScoreCalculator.Calculate(
                job.Priority, job.ScheduledAt, job.CreatedAt);

            // NX flag: only add if member does not already exist
            var added = await _queue.EnqueueReadyIfAbsent(job.Id, score, ct);

            if (added)
            {
                recovered++;
                _logger.LogWarning(
                    "Orphaned job recovered and re-enqueued. " +
                    "JobId={JobId} ScheduledAt={ScheduledAt} RetryCount={RetryCount}",
                    job.Id, job.ScheduledAt, job.RetryCount);
            }
        }

        if (recovered > 0)
            _logger.LogInformation(
                "Orphan recovery complete. Recovered={Count}", recovered);
    }
}