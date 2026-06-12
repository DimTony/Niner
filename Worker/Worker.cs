using Core.DTOs;
using Core.Enums;
using Core.Interfaces;
using Core.Options;
using Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Worker;

public class WorkerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IJobQueueService _queue;
    private readonly IEventPublisher _events;
    private readonly IJobHandlerFactory _handlerFactory;
    private readonly IAlertService _alerts;
    private readonly WorkerOptions _options;
    private readonly ILogger<WorkerService> _logger;

    public WorkerService(
        IServiceScopeFactory scopeFactory,
        IJobQueueService queue,
        IEventPublisher events,
        IJobHandlerFactory handlerFactory,
        IAlertService alerts,
        IOptions<WorkerOptions> options,
        ILogger<WorkerService> logger)
    {
        _scopeFactory    = scopeFactory;
        _queue           = queue;
        _events          = events;
        _handlerFactory  = handlerFactory;
        _alerts          = alerts;
        _options         = options.Value;
        _logger          = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "Worker started. WorkerId={WorkerId}", _options.WorkerId);

        // Run stale lock recovery and main poll loop concurrently
        await Task.WhenAll(
            RunHeartbeatLoop(ct),
            RunStaleLockRecovery(ct),
            RunPollLoop(ct));
    }

    // --------------------------------------------------
    // Heartbeat — proves this worker is alive
    // --------------------------------------------------

    private async Task RunHeartbeatLoop(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds);
        var ttl = TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds * 3);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _queue.SetWorkerHeartbeat(_options.WorkerId, ttl, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Heartbeat failed. WorkerId={WorkerId}",
                    _options.WorkerId);
            }

            await Task.Delay(interval, ct);
        }
    }

    // --------------------------------------------------
    // Stale lock recovery — reclaims jobs from dead workers
    // --------------------------------------------------

    private async Task RunStaleLockRecovery(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RecoverStaleLocks(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stale lock recovery error.");
            }

            // Check every 30 seconds
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task RecoverStaleLocks(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var logRepo = scope.ServiceProvider.GetRequiredService<IJobLogRepository>();

        var threshold = DateTimeOffset.UtcNow
            .AddMinutes(-_options.StaleLockThresholdMinutes);

        var staleJobs = await jobRepo.GetStaleLockJobs(threshold, ct);

        foreach (var job in staleJobs)
        {
            _logger.LogWarning(
                "Recovering stale job. JobId={JobId} LockedBy={LockedBy} LockedAt={LockedAt}",
                job.Id, job.LockedBy, job.LockedAt);

            job.Status    = JobStatus.Pending;
            job.LockedBy  = null;
            job.LockedAt  = null;

            await jobRepo.Update(job, ct);

            // Re-enqueue it
            var score = JobScoreCalculator.Calculate(
                job.Priority, job.ScheduledAt, job.CreatedAt);
            await _queue.EnqueueReady(job.Id, score, ct);

            await logRepo.Create(
                job.Id,
                LogEvent.RetryAttempted,
                $"Job recovered from stale lock. Previously locked by {job.LockedBy}.",
                new { recoveredAt = DateTimeOffset.UtcNow },
                ct);
        }
    }

    // --------------------------------------------------
    // Main poll loop
    // --------------------------------------------------

    private async Task RunPollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var jobId = await _queue.DequeueNext(ct);

                if (jobId is null)
                {
                    await Task.Delay(_options.PollingIntervalMs, ct);
                    continue;
                }

                // Fire and forget — process without blocking the poll loop
                _ = Task.Run(() => ProcessJob(jobId.Value, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Poll loop error.");
                await Task.Delay(_options.PollingIntervalMs, ct);
            }
        }
    }

    // --------------------------------------------------
    // Job processing
    // --------------------------------------------------

    private async Task ProcessJob(Guid jobId, CancellationToken ct)
    {
        var lockTtl = TimeSpan.FromSeconds(_options.LockTtlSeconds);

        // Acquire Redis lock — duplicate protection
        var acquired = await _queue.AcquireLock(
            jobId, _options.WorkerId, lockTtl, ct);

        if (!acquired)
        {
            _logger.LogDebug(
                "Lock not acquired, skipping. JobId={JobId}", jobId);
            return;
        }

        using var scope      = _scopeFactory.CreateScope();
        var jobRepo          = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var logRepo          = scope.ServiceProvider.GetRequiredService<IJobLogRepository>();
        var dlqRepo          = scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();

        CancellationTokenSource? heartbeatCts = null;

        try
        {
            var job = await jobRepo.GetByIdWithDependencies(jobId, ct);

            // Job may have been soft-deleted or cancelled between dequeue and now
            if (job is null)
            {
                _logger.LogWarning("Job not found after dequeue. JobId={JobId}", jobId);
                await _queue.ReleaseLock(jobId, _options.WorkerId, ct);
                return;
            }

            // Cancelled before pickup
            if (job.Status == JobStatus.Cancelled)
            {
                _logger.LogInformation(
                    "Job already cancelled, skipping. JobId={JobId}", jobId);
                await _queue.ReleaseLock(jobId, _options.WorkerId, ct);
                return;
            }

            // DAG check — are all dependencies completed?
            var hasBlockingDeps = job.Dependencies
                .Any(d => d.DependsOn.Status != JobStatus.Completed);

            if (hasBlockingDeps)
            {
                _logger.LogInformation(
                    "Job has unresolved dependencies, re-blocking. JobId={JobId}", jobId);

                job.Status = JobStatus.Blocked;
                await jobRepo.Update(job, ct);
                await _queue.ReleaseLock(jobId, _options.WorkerId, ct);
                return;
            }

            // Claim the job
            job.Status   = JobStatus.Processing;
            job.LockedBy = _options.WorkerId;
            job.LockedAt = DateTimeOffset.UtcNow;
            await jobRepo.Update(job, ct);

            await logRepo.Create(
                job.Id, LogEvent.Started,
                $"Job picked up by worker {_options.WorkerId}.",
                new { workerId = _options.WorkerId },
                ct);

            await _events.PublishJobEvent(job.Id, "processing", ct);

            // Start lock heartbeat — renews lock every HeartbeatIntervalSeconds
            heartbeatCts = new CancellationTokenSource();
            _ = Task.Run(
                () => RunLockHeartbeat(jobId, heartbeatCts.Token),
                heartbeatCts.Token);

            // Re-check for race: cancel request arrived between dequeue and claim
            var current = await jobRepo.GetById(job.Id, ct);
            if (current is { Status: JobStatus.Cancelled })
            {
                heartbeatCts.Cancel();
                 
                await logRepo.Create(
                    job.Id, LogEvent.Cancelled,
                    "Job cancelled before execution started.",
                    null, ct);
                await _events.PublishJobEvent(job.Id, "cancelled", ct);
                await _queue.ReleaseLock(job.Id, _options.WorkerId, ct);
                return;
            }

            // Execute handler
            // TODO: Implement mid-execution cancellation (stopping the Task.Delay or SMTP call partway through), may require threading a per-job CancellationToken into IJobHandler.Execute, sourced from polling the DB or a Redis pub/sub channel during execution
            var handler = _handlerFactory.Resolve(job.Type);
            var result  = await handler.Execute(job.Payload, ct);

            // Stop heartbeat
            await heartbeatCts.CancelAsync();

            // Checkpoint — re-check if cancellation was requested while handler ran
            var freshJob = await jobRepo.GetById(job.Id, ct);
            if (freshJob is { Status: JobStatus.Cancelled })
            {
                await logRepo.Create(
                    job.Id, LogEvent.Cancelled,
                    "Job cancellation honored after handler completed; result discarded.",
                    new { discardedSuccess = result.Success },
                    ct);
            
                await _events.PublishJobEvent(job.Id, "cancelled", ct);
                await _queue.ReleaseLock(job.Id, _options.WorkerId, ct);
                return;
            }

            if (result.Success)
                await HandleSuccess(job, result, jobRepo, logRepo, dlqRepo, ct);
            else
                await HandleFailure(job, result.ErrorMessage!, jobRepo, logRepo, dlqRepo, ct);
        }
        catch (Exception ex)
        {
            heartbeatCts?.Cancel();

            _logger.LogError(ex,
                "Unhandled exception processing job. JobId={JobId}", jobId);

            // Treat unhandled exceptions as job failure
            using var errorScope = _scopeFactory.CreateScope();
            var jobRepo2 = errorScope.ServiceProvider.GetRequiredService<IJobRepository>();
            var logRepo2 = errorScope.ServiceProvider.GetRequiredService<IJobLogRepository>();
            var dlqRepo2 = errorScope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();

            var job = await jobRepo2.GetById(jobId, ct);
            if (job is not null)
                await HandleFailure(
                    job, ex.Message, jobRepo2, logRepo2, dlqRepo2, ct);

            await _queue.ReleaseLock(jobId, _options.WorkerId, ct);
        }
    }

    // --------------------------------------------------
    // Lock heartbeat — keeps lock alive during processing
    // --------------------------------------------------

    private async Task RunLockHeartbeat(Guid jobId, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_options.HeartbeatIntervalSeconds);
        var ttl      = TimeSpan.FromSeconds(_options.LockTtlSeconds);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct);

            if (ct.IsCancellationRequested) break;

            var renewed = await _queue.RenewLock(
                jobId, _options.WorkerId, ttl, ct);

            if (!renewed)
            {
                _logger.LogWarning(
                    "Lock renewal failed — lock may have been claimed by another worker. " +
                    "JobId={JobId} WorkerId={WorkerId}",
                    jobId, _options.WorkerId);
            }
        }
    }

    // --------------------------------------------------
    // Success path
    // --------------------------------------------------

    private async Task HandleSuccess(
        Core.Entities.Job job,
        JobHandlerResult result,
        IJobRepository jobRepo,
        IJobLogRepository logRepo,
        IDeadLetterRepository dlqRepo,
        CancellationToken ct)
    {
        job.Status   = JobStatus.Completed;
        job.LockedBy = null;
        job.LockedAt = null;
        // await jobRepo.Update(job, ct);
        await SafeUpdate(job, jobRepo, ct);

        await logRepo.Create(
            job.Id, LogEvent.Completed,
            "Job completed successfully.",
            result.ResultData,
            ct);

        await _events.PublishJobEvent(job.Id, "completed", ct);
        await _queue.ReleaseLock(job.Id, _options.WorkerId, ct);

        _logger.LogInformation(
            "Job completed. JobId={JobId} Type={Type} WorkerId={WorkerId}",
            job.Id, job.Type, _options.WorkerId);

        // Unblock DAG dependents
        await UnblockDependents(job.Id, jobRepo, logRepo, ct);

        // Schedule next recurrence
        if (job.Recurrence.HasValue)
            await ScheduleNextRecurrence(job, jobRepo, logRepo, ct);
    }

    // --------------------------------------------------
    // Failure path
    // --------------------------------------------------

    private async Task HandleFailure(
        Core.Entities.Job job,
        string error,
        IJobRepository jobRepo,
        IJobLogRepository logRepo,
        IDeadLetterRepository dlqRepo,
        CancellationToken ct)
    {
        job.RetryCount++;
        job.LastError = error;

        if (job.RetryCount < job.MaxRetries)
        {
            // Schedule retry with backoff + jitter
            var delay       = RetryCalculator.GetDelay(job.RetryCount);
            var retryAt     = DateTimeOffset.UtcNow.Add(delay);

            job.Status   = JobStatus.Pending;
            job.LockedBy = null;
            job.LockedAt = null;
            job.ScheduledAt = retryAt;

            // await jobRepo.Update(job, ct);
            await SafeUpdate(job, jobRepo, ct);

            try
            {
                await _queue.EnqueueScheduled(job.Id, retryAt, ct);
            }
            catch (Exception ex)
            {
                // Postgres is already updated — orphan recovery will rescue this job
                // within 60 seconds. Log loudly so it is visible.
                _logger.LogError(ex,
                    "CRITICAL: Failed to enqueue retry in Redis. " +
                    "Job is pending in Postgres and will be recovered by orphan recovery loop. " +
                    "JobId={JobId} RetryAt={RetryAt}",
                    job.Id, retryAt);
            }

            await logRepo.Create(
                job.Id, LogEvent.RetryAttempted,
                $"Retry {job.RetryCount}/{job.MaxRetries} scheduled in {delay.TotalSeconds:F1}s. Error: {error}",
                new { attempt = job.RetryCount, delaySeconds = delay.TotalSeconds, error },
                ct);

            await _events.PublishJobEvent(job.Id, "pending", ct);

            _logger.LogWarning(
                "Job failed, retry scheduled. JobId={JobId} Attempt={Attempt} RetryAt={RetryAt} Error={Error}",
                job.Id, job.RetryCount, retryAt, error);
        }
        else
        {
            // Exhausted all retries — move to DLQ
            job.Status   = JobStatus.Failed;
            job.LockedBy = null;
            job.LockedAt = null;
            await jobRepo.Update(job, ct);

            // var dlqEntry = new Core.Entities.DeadLetterEntry
            // {
            //     Id           = Guid.NewGuid(),
            //     JobId        = job.Id,
            //     ErrorDetails = error,
            //     FailureCount = job.RetryCount,
            //     Resolved     = false
            // };

            // await dlqRepo.Create(dlqEntry, ct);
            await dlqRepo.Upsert(job.Id, error, job.RetryCount, ct);

            // Increment DLQ counter and check alert threshold
            var dlqCount = await _queue.IncrementDlqCount(ct);
            if (dlqCount >= _options.DlqAlertThreshold)
            {
                await _alerts.SendDlqThresholdAlert((int)dlqCount, ct);
                await _queue.ResetDlqCount(ct);
            }

            await logRepo.Create(
                job.Id, LogEvent.Failed,
                $"Job failed after {job.RetryCount} attempts. Moved to DLQ. Error: {error}",
                new { finalError = error, attempts = job.RetryCount },
                ct);

            await _events.PublishJobEvent(job.Id, "failed", ct);
            await _queue.ReleaseLock(job.Id, _options.WorkerId, ct);

            _logger.LogError(
                "Job moved to DLQ. JobId={JobId} Attempts={Attempts} Error={Error}",
                job.Id, job.RetryCount, error);
        }
    }

    // --------------------------------------------------
    // DAG — unblock dependents after completion
    // --------------------------------------------------

    private async Task UnblockDependents(
        Guid completedJobId,
        IJobRepository jobRepo,
        IJobLogRepository logRepo,
        CancellationToken ct)
    {
        var unblocked = await jobRepo.GetUnblockedDependents(completedJobId, ct);

        foreach (var dep in unblocked)
        {
            dep.Status = JobStatus.Pending;
            await jobRepo.Update(dep, ct);

            var score = JobScoreCalculator.Calculate(
                dep.Priority, dep.ScheduledAt, dep.CreatedAt);
            await _queue.EnqueueReady(dep.Id, score, ct);

            await logRepo.Create(
                dep.Id, LogEvent.Created,
                $"Job unblocked after dependency {completedJobId} completed.",
                new { unlockedBy = completedJobId },
                ct);

            await _events.PublishJobEvent(dep.Id, "pending", ct);

            _logger.LogInformation(
                "Dependent job unblocked. JobId={JobId} UnblockedBy={CompletedJobId}",
                dep.Id, completedJobId);
        }
    }

    // --------------------------------------------------
    // Recurrence — schedule next run after completion
    // --------------------------------------------------

    private async Task ScheduleNextRecurrence(
        Core.Entities.Job completedJob,
        IJobRepository jobRepo,
        IJobLogRepository logRepo,
        CancellationToken ct)
    {
        var interval = completedJob.Recurrence switch
        {
            JobRecurrence.Every1Minute  => TimeSpan.FromMinutes(1),
            JobRecurrence.Every5Minutes => TimeSpan.FromMinutes(5),
            JobRecurrence.Every1Hour    => TimeSpan.FromHours(1),
            _ => throw new InvalidOperationException(
                $"Unknown recurrence: {completedJob.Recurrence}")
        };

        var nextRun = DateTimeOffset.UtcNow.Add(interval);

        var nextJob = new Core.Entities.Job
        {
            Id          = Guid.NewGuid(),
            Type        = completedJob.Type,
            Payload     = completedJob.Payload,
            Priority    = completedJob.Priority,
            Status      = JobStatus.Pending,
            RetryCount  = 0,
            MaxRetries  = completedJob.MaxRetries,
            ScheduledAt = nextRun,
            Recurrence  = completedJob.Recurrence,
            CreatedAt   = DateTimeOffset.UtcNow,
            UpdatedAt   = DateTimeOffset.UtcNow
        };

        await jobRepo.Create(nextJob, ct);
        await _queue.EnqueueScheduled(nextJob.Id, nextRun, ct);

        await logRepo.Create(
            nextJob.Id, LogEvent.Created,
            $"Recurring job scheduled. Next run at {nextRun:O}. Parent job: {completedJob.Id}.",
            new { parentJobId = completedJob.Id, nextRun, interval = interval.ToString() },
            ct);

        _logger.LogInformation(
            "Next recurrence scheduled. NewJobId={NewJobId} NextRun={NextRun} Interval={Interval}",
            nextJob.Id, nextRun, interval);
    }

    private async Task SafeUpdate(Core.Entities.Job job, IJobRepository jobRepo, CancellationToken ct)
    {
        try
        {
            await jobRepo.Update(job, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("Concurrency conflict updating job {JobId}; reloading and re-applying.", job.Id);

            var fresh = await jobRepo.GetById(job.Id, ct)
                ?? throw new InvalidOperationException($"Job {job.Id} disappeared during concurrency retry.");

            // Re-apply this call's intended changes onto the fresh row
            fresh.Status      = job.Status;
            fresh.RetryCount  = job.RetryCount;
            fresh.MaxRetries  = job.MaxRetries;
            fresh.LastError   = job.LastError;
            fresh.LockedBy    = job.LockedBy;
            fresh.LockedAt    = job.LockedAt;
            fresh.ScheduledAt = job.ScheduledAt;

            await jobRepo.Update(fresh, ct);
        }
    }
}
