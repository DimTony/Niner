using Api.DTOs;
using Core.Entities;

namespace Api.Mapping;

public static class JobMapper
{
    public static JobResponse ToResponse(Job job)
    {
        return new JobResponse
        {
            Id          = job.Id,
            Type        = job.Type,
            Payload     = job.Payload,
            Priority    = job.Priority,
            Status      = job.Status,
            RetryCount  = job.RetryCount,
            MaxRetries  = job.MaxRetries,
            ScheduledAt = job.ScheduledAt,
            Recurrence  = job.Recurrence,
            CreatedAt   = job.CreatedAt,
            UpdatedAt   = job.UpdatedAt,
            LastError   = job.LastError,
            DependsOn   = job.Dependencies
                .Select(d => d.DependsOnId)
                .ToList()
        };
    }

    public static DlqEntryResponse ToResponse(DeadLetterEntry entry)
    {
        return new DlqEntryResponse
        {
            Id           = entry.Id,
            JobId        = entry.JobId,
            ErrorDetails = entry.ErrorDetails,
            FailureCount = entry.FailureCount,
            CreatedAt    = entry.CreatedAt,
            ResolvedAt   = entry.ResolvedAt,
            Resolved     = entry.Resolved,
            Job          = entry.Job is not null
                ? ToResponse(entry.Job)
                : null
        };
    }
}