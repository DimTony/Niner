using Core.Enums;

namespace Core.Entities;

public class JobLog
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public LogEvent Event { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Navigation
    public Job Job { get; set; } = null!;
}