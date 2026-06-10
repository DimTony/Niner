using System.ComponentModel.DataAnnotations;
using Core.Enums;

namespace Api.DTOs;

public class CreateJobRequest
{
    [Required]
    public JobType Type { get; set; }

    [Required]
    public string Payload { get; set; } = string.Empty;

    [Required]
    public JobPriority Priority { get; set; }

    public DateTimeOffset? ScheduledAt { get; set; }

    public JobRecurrence? Recurrence { get; set; }

    public List<Guid>? DependsOn { get; set; }
}