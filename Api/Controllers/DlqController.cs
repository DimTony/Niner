using Api.Mapping;
using Api.DTOs;
using Core.Enums;
using Core.Interfaces;
using Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/dlq")]
[Produces("application/json")]
public class DlqController : ControllerBase
{
    private readonly IDeadLetterRepository _dlqRepo;
    private readonly IJobRepository        _jobRepo;
    private readonly IJobLogRepository     _logRepo;
    private readonly IJobQueueService      _queue;
    private readonly IEventPublisher       _events;

    public DlqController(
        IDeadLetterRepository dlqRepo,
        IJobRepository jobRepo,
        IJobLogRepository logRepo,
        IJobQueueService queue,
        IEventPublisher events)
    {
        _dlqRepo = dlqRepo;
        _jobRepo = jobRepo;
        _logRepo = logRepo;
        _queue   = queue;
        _events  = events;
    }

    /// <summary>Lists all unresolved DLQ entries.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<DlqEntryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDlq(CancellationToken ct)
    {
        var entries = await _dlqRepo.GetUnresolved(ct);
        return Ok(entries.Select(JobMapper.ToResponse));
    }

    /// <summary>Manually retries a job from the DLQ.</summary>
    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(typeof(JobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RetryJob(Guid id, CancellationToken ct)
    {
        var entry = await _dlqRepo.GetByJobId(id, ct);
        if (entry is null)
            return NotFound(new { error = "DLQ entry not found." });

        var job = await _jobRepo.GetById(entry.JobId, ct);
        if (job is null)
            return NotFound(new { error = "Job not found." });

        // Reset job for retry
        job.Status     = JobStatus.Pending;
        job.RetryCount = 0;
        job.LastError  = null;
        job.LockedBy   = null;
        job.LockedAt   = null;
        job.ScheduledAt = DateTimeOffset.UtcNow;

        await _jobRepo.Update(job, ct);

        // Mark DLQ entry resolved
        await _dlqRepo.MarkResolved(entry.Id, ct);
        await _queue.DecrementDlqCount(ct);

        // Re-enqueue
        var score = JobScoreCalculator.Calculate(
            job.Priority, job.ScheduledAt, job.CreatedAt);
        await _queue.EnqueueReady(job.Id, score, ct);

        await _logRepo.Create(
            job.Id, LogEvent.RetryAttempted,
            "Job manually retried from DLQ.",
            new { retriedAt = DateTimeOffset.UtcNow },
            ct);

        await _events.PublishJobEvent(job.Id, "pending", ct);

        return Ok(JobMapper.ToResponse(job));
    }
}