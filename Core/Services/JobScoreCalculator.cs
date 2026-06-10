using Core.Enums;

namespace Core.Services;

public static class JobScoreCalculator
{
    private const long PriorityStep = 1_000_000_000_000L;

    public static double Calculate(
        JobPriority priority,
        DateTimeOffset scheduledAt,
        DateTimeOffset createdAt,
        JobPriority? effectivePriority = null)
    {
        var p = effectivePriority ?? priority;

        long priorityComponent = (long)(p - 1) * PriorityStep;
        long timeComponent = scheduledAt.ToUnixTimeMilliseconds();
        long tiebreaker = createdAt.ToUnixTimeMilliseconds() / 1_000_000;

        return priorityComponent + timeComponent + tiebreaker;
    }

    public static JobPriority GetEffectivePriority(
        JobPriority actual,
        DateTimeOffset enqueuedAt,
        DateTimeOffset asOf)
    {
        var waitMinutes = (asOf - enqueuedAt).TotalMinutes;

        return waitMinutes switch
        {
            >= 10 => JobPriority.High,
            >= 5  => actual == JobPriority.Low ? JobPriority.Medium : actual,
            _     => actual
        };
    }
}