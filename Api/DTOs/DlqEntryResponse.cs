namespace Api.DTOs;

public class DlqEntryResponse
{
    public Guid           Id           { get; set; }
    public Guid           JobId        { get; set; }
    public string         ErrorDetails { get; set; } = string.Empty;
    public int            FailureCount { get; set; }
    public DateTimeOffset CreatedAt    { get; set; }
    public DateTimeOffset? ResolvedAt  { get; set; }
    public bool           Resolved     { get; set; }
    public JobResponse?   Job          { get; set; }
}