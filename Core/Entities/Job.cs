using Core.Enums;

namespace Core.Entities;

public class Job
{
    public Guid Id { get; set; }
    public JobType Type { get; set; }
    public string Payload { get; set; } = string.Empty;
    public JobPriority Priority { get; set; }
    public JobStatus Status { get; set; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public JobRecurrence? Recurrence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? LockedBy { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public string? LastError { get; set; }

    // Navigation
    public ICollection<JobDependency> Dependencies { get; set; } = new List<JobDependency>();
    public ICollection<JobDependency> Dependents { get; set; } = new List<JobDependency>();
    public ICollection<JobLog> Logs { get; set; } = new List<JobLog>();
    public DeadLetterEntry? DeadLetterEntry { get; set; }

    // private Job() { }

    // public static Job Create()
    //     => new()
    //     {
    //     };
}