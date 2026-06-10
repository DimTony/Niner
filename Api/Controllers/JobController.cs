using Api.Mapping;
using Api.DTOs;
using Core.Entities;
using Core.Enums;
using Core.Interfaces;
using Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[ApiController]
[Route("api/jobs")]
[Produces("application/json")]
public class JobsController : ControllerBase
{
    private readonly IJobRepository    _jobRepo;
    private readonly IJobLogRepository _logRepo;
    private readonly IJobQueueService  _queue;
    private readonly IEventPublisher   _events;

    public JobsController(
        IJobRepository jobRepo,
        IJobLogRepository logRepo,
        IJobQueueService queue,
        IEventPublisher events)
    {
        _jobRepo = jobRepo;
        _logRepo = logRepo;
        _queue   = queue;
        _events  = events;
    }

    /// <summary>Creates a new job.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(JobResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateJob(
        [FromBody] CreateJobRequest request,
        CancellationToken ct)
    {
        // Validate payload is valid JSON
        try
        {
            System.Text.Json.JsonDocument.Parse(request.Payload);
        }
        catch
        {
            return BadRequest(new { error = "Payload must be valid JSON." });
        }

        var hasDependencies = request.DependsOn?.Count > 0;
        var scheduledAt     = request.ScheduledAt ?? DateTimeOffset.UtcNow;
        var isFuture        = scheduledAt > DateTimeOffset.UtcNow.AddSeconds(1);

        var job = new Job
        {
            Id          = Guid.NewGuid(),
            Type        = request.Type,
            Payload     = request.Payload,
            Priority    = request.Priority,
            Status      = hasDependencies ? JobStatus.Blocked : JobStatus.Pending,
            RetryCount  = 0,
            MaxRetries  = 3,
            ScheduledAt = scheduledAt,
            Recurrence  = request.Recurrence,
            CreatedAt   = DateTimeOffset.UtcNow,
            UpdatedAt   = DateTimeOffset.UtcNow
        };

        // Attach dependencies
        if (hasDependencies)
        {
            foreach (var depId in request.DependsOn!)
            {
                var dep = await _jobRepo.GetById(depId, ct);
                if (dep is null)
                    return BadRequest(new { error = $"Dependency job {depId} not found." });

                job.Dependencies.Add(new JobDependency
                {
                    JobId       = job.Id,
                    DependsOnId = depId
                });
            }
        }

        await _jobRepo.Create(job, ct);

        await _logRepo.Create(
            job.Id, LogEvent.Created,
            $"Job created. Type={job.Type} Priority={job.Priority} " +
            $"ScheduledAt={job.ScheduledAt:O} HasDependencies={hasDependencies}",
            new { type = job.Type, priority = job.Priority },
            ct);

        // Queue immediately if ready, otherwise into scheduled set
        if (!hasDependencies)
        {
            if (isFuture)
                await _queue.EnqueueScheduled(job.Id, scheduledAt, ct);
            else
            {
                var score = JobScoreCalculator.Calculate(
                    job.Priority, job.ScheduledAt, job.CreatedAt);
                await _queue.EnqueueReady(job.Id, score, ct);
            }
        }

        await _events.PublishJobEvent(job.Id, job.Status.ToString().ToLower(), ct);

        return CreatedAtAction(
            nameof(GetJob),
            new { id = job.Id },
            JobMapper.ToResponse(job));
    }

    /// <summary>Gets a job by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(JobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJob(Guid id, CancellationToken ct)
    {
        var job = await _jobRepo.GetByIdWithDependencies(id, ct);
        if (job is null) return NotFound();
        return Ok(JobMapper.ToResponse(job));
    }

    /// <summary>Lists jobs with optional status filter and pagination.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<JobResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListJobs(
        [FromQuery] JobStatus? status,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct     = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(page, 1);

        var jobs = await _jobRepo.GetAll(status, page, pageSize, ct);

        return Ok(new PagedResponse<JobResponse>
        {
            Items    = jobs.Select(JobMapper.ToResponse).ToList(),
            Page     = page,
            PageSize = pageSize,
            Total    = jobs.Count
        });
    }

    /// <summary>Cancels a pending or processing job.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(JobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelJob(Guid id, CancellationToken ct)
    {
        var job = await _jobRepo.GetById(id, ct);
        if (job is null) return NotFound();

        if (job.Status is JobStatus.Completed or
                          JobStatus.Failed or
                          JobStatus.Cancelled)
        {
            return Conflict(new
            {
                error = $"Cannot cancel a job with status {job.Status}."
            });
        }

        // If pending — cancel immediately and remove from queue
        if (job.Status == JobStatus.Pending)
        {
            job.Status = JobStatus.Cancelled;
            await _jobRepo.Update(job, ct);
            await _queue.RemoveFromReady(id, ct);
            await _queue.RemoveFromScheduled(id, ct);
        }
        else
        {
            // Processing — mark for graceful cancellation
            // Worker checks this on its next checkpoint
            job.Status = JobStatus.Cancelled;
            await _jobRepo.Update(job, ct);
        }

        await _logRepo.Create(
            job.Id, LogEvent.Cancelled,
            $"Job cancelled via API. Previous status: {job.Status}.",
            null, ct);

        await _events.PublishJobEvent(job.Id, "cancelled", ct);

        return Ok(JobMapper.ToResponse(job));
    }

    /// <summary>Gets structured logs for a job.</summary>
    [HttpGet("{id:guid}/logs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetJobLogs(Guid id, CancellationToken ct)
    {
        var job = await _jobRepo.GetById(id, ct);
        if (job is null) return NotFound();

        var logs = await _logRepo.GetByJobId(id, ct);

        return Ok(logs.Select(l => new
        {
            l.Id,
            l.JobId,
            Event    = l.Event.ToString(),
            l.Message,
            Metadata = l.Metadata,
            l.CreatedAt
        }));
    }

    /// <summary>Gets job status counts for the dashboard.</summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(DashboardResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        var counts = await _jobRepo.GetStatusCounts(ct);

        return Ok(new DashboardResponse
        {
            StatusCounts = counts.ToDictionary(
                k => k.Key.ToString().ToLower(),
                v => v.Value),
            GeneratedAt = DateTimeOffset.UtcNow
        });
    }
}