namespace Core.Entities;

public class DeadLetterEntry
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string ErrorDetails { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public bool Resolved { get; set; }

    // Navigation
    public Job Job { get; set; } = null!;
}