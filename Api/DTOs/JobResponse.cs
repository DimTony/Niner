using Core.Enums;

namespace Api.DTOs;

public class JobResponse
{
    public Guid            Id          { get; set; }
    public JobType         Type        { get; set; }
    public string          Payload     { get; set; } = string.Empty;
    public JobPriority     Priority    { get; set; }
    public JobStatus       Status      { get; set; }
    public int             RetryCount  { get; set; }
    public int             MaxRetries  { get; set; }
    public DateTimeOffset  ScheduledAt { get; set; }
    public JobRecurrence?  Recurrence  { get; set; }
    public DateTimeOffset  CreatedAt   { get; set; }
    public DateTimeOffset  UpdatedAt   { get; set; }
    public string?         LastError   { get; set; }
    public List<Guid>?     DependsOn   { get; set; }
}