namespace Core.Options;

public class SchedulerOptions
{
    public const string Section = "Scheduler";

    public int PromotionIntervalMs      { get; set; } = 500;
    public int AgingIntervalSeconds     { get; set; } = 30;
    public int AgingMediumThresholdMins { get; set; } = 5;
    public int AgingHighThresholdMins   { get; set; } = 10;
    public int WheelSizeSlots           { get; set; } = 3600;
    public int WheelTickIntervalMs      { get; set; } = 1000;
    public int BenchmarkJobCount        { get; set; } = 10_000;
}