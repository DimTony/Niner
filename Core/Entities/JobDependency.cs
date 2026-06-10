namespace Core.Entities;

public class JobDependency
{
    public Guid JobId { get; set; }
    public Guid DependsOnId { get; set; }

    // Navigation
    public Job Job { get; set; } = null!;
    public Job DependsOn { get; set; } = null!;
}