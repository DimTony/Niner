using Api.Mapping;
using Api.DTOs;
using Core.Enums;
using Core.Interfaces;
using Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Api.Controllers;

[ApiController]
[Route("api/dlq")]
[Produces("application/json")]
public class DlqController : ControllerBase
{
    private readonly IDeadLetterRepository _dlqRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IJobLogRepository _logRepo;
    private readonly IJobQueueService _queue;
    private readonly IEventPublisher _events;
    private readonly ILogger<DlqController> _logger;

    public DlqController(
        IDeadLetterRepository dlqRepo,
        IJobRepository jobRepo,
        IJobLogRepository logRepo,
        IJobQueueService queue,
        IEventPublisher events,
        ILogger<DlqController> logger)
    {
        _dlqRepo = dlqRepo;
        _jobRepo = jobRepo;
        _logRepo = logRepo;
        _queue = queue;
        _events = events;
        _logger = logger;
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
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RetryJob(Guid id, CancellationToken ct)
    {
        var entry = await _dlqRepo.GetByJobId(id, ct);
        if (entry is null)
            return NotFound(new { error = "DLQ entry not found." });

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var job = await _jobRepo.GetById(entry.JobId, ct);
            if (job is null)
                return NotFound(new { error = "Job not found." });

            job.Status = JobStatus.Pending;
            job.RetryCount = 0;
            job.LastError = null;
            job.LockedBy = null;
            job.LockedAt = null;
            job.ScheduledAt = DateTimeOffset.UtcNow;

            try
            {
                await _jobRepo.Update(job, ct);

                await _dlqRepo.MarkResolved(entry.Id, ct);
                await _queue.DecrementDlqCount(ct);

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
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                _logger.LogWarning("Concurrency conflict retrying job {JobId} from DLQ (attempt {Attempt}).", entry.JobId, attempt + 1);
            }
        }

        return Conflict(new { error = "Failed to retry job due to concurrent updates. Please try again." });
    }
}