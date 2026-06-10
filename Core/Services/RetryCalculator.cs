namespace Core.Services;

public static class RetryCalculator
{
    // Base delays per attempt: 1s, 5s, 25s
    private static readonly double[] BasePowers = [1, 5, 25];
    private static readonly Random _rng = new();

    public static TimeSpan GetDelay(int attemptNumber)
    {
        // attemptNumber is 1-indexed (first retry = 1)
        var index = Math.Clamp(attemptNumber - 1, 0, BasePowers.Length - 1);
        var baseSeconds = BasePowers[index];

        // Jitter: ±20% of base
        var jitter = baseSeconds * 0.20 * (_rng.NextDouble() * 2 - 1);
        var totalSeconds = Math.Max(0.1, baseSeconds + jitter);

        return TimeSpan.FromSeconds(totalSeconds);
    }
}