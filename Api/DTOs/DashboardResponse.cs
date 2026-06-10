namespace Api.DTOs;

public class DashboardResponse
{
    public Dictionary<string, int> StatusCounts { get; set; } = new();
    public DateTimeOffset          GeneratedAt  { get; set; }
}