namespace Core.Options;

public class WorkerOptions
{
    public const string Section = "Worker";

    public int PollingIntervalMs        { get; set; } = 1000;
    public int LockTtlSeconds           { get; set; } = 30;
    public int HeartbeatIntervalSeconds { get; set; } = 10;
    public int StaleLockThresholdMinutes{ get; set; } = 2;
    public int DlqAlertThreshold        { get; set; } = 10;
    public string WorkerId              { get; set; } = $"worker_{Guid.NewGuid():N}";
}