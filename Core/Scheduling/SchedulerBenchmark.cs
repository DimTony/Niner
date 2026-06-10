using System.Diagnostics;
using Core.Enums;
using Core.Services;

namespace Core.Scheduling;

public record BenchmarkResult(
    string Algorithm,
    int JobCount,
    long InsertionMs,
    long PromotionMs,
    long TotalMs,
    double InsertionsPerSecond,
    double PromotionsPerSecond);

public class SchedulerBenchmark
{
    public (BenchmarkResult Heap, BenchmarkResult Wheel) Run(int jobCount)
    {
        var jobs = GenerateJobs(jobCount);
        var heapResult  = BenchmarkHeap(jobs);
        var wheelResult = BenchmarkWheel(jobs);
        return (heapResult, wheelResult);
    }

    // --------------------------------------------------
    // Heap benchmark
    // --------------------------------------------------

    private BenchmarkResult BenchmarkHeap(
        List<(Guid Id, JobPriority Priority, DateTimeOffset ScheduledAt, DateTimeOffset CreatedAt)> jobs)
    {
        var heap = new JobHeap();
        var sw   = Stopwatch.StartNew();

        // Insertion
        foreach (var (id, priority, scheduledAt, createdAt) in jobs)
        {
            var score = JobScoreCalculator.Calculate(priority, scheduledAt, createdAt);
            heap.Push(new HeapEntry(id, score));
        }

        var insertionMs = sw.ElapsedMilliseconds;
        sw.Restart();

        // Promotion — pop all
        var promoted = 0;
        while (heap.Pop() is not null)
            promoted++;

        var promotionMs = sw.ElapsedMilliseconds;
        var totalMs     = insertionMs + promotionMs;

        return new BenchmarkResult(
            Algorithm: "MinHeap",
            JobCount: jobs.Count,
            InsertionMs: insertionMs,
            PromotionMs: promotionMs,
            TotalMs: totalMs,
            InsertionsPerSecond: jobs.Count / Math.Max(insertionMs / 1000.0, 0.001),
            PromotionsPerSecond: promoted  / Math.Max(promotionMs / 1000.0, 0.001));
    }

    // --------------------------------------------------
    // Timing wheel benchmark
    // --------------------------------------------------

    private BenchmarkResult BenchmarkWheel(
        List<(Guid Id, JobPriority Priority, DateTimeOffset ScheduledAt, DateTimeOffset CreatedAt)> jobs)
    {
        var wheel = new TimingWheel(3600);
        var sw    = Stopwatch.StartNew();

        // Insertion
        foreach (var (id, _, scheduledAt, _) in jobs)
            wheel.AddJob(id, scheduledAt);

        var insertionMs = sw.ElapsedMilliseconds;
        sw.Restart();

        // Promotion — tick through all slots
        var promoted = 0;
        for (var i = 0; i < wheel.SlotCount; i++)
            promoted += wheel.Tick().Count;

        var promotionMs = sw.ElapsedMilliseconds;
        var totalMs     = insertionMs + promotionMs;

        return new BenchmarkResult(
            Algorithm: "TimingWheel",
            JobCount: jobs.Count,
            InsertionMs: insertionMs,
            PromotionMs: promotionMs,
            TotalMs: totalMs,
            InsertionsPerSecond: jobs.Count / Math.Max(insertionMs / 1000.0, 0.001),
            PromotionsPerSecond: promoted  / Math.Max(promotionMs / 1000.0, 0.001));
    }

    // --------------------------------------------------
    // Generate synthetic job data
    // --------------------------------------------------

    private static List<(Guid, JobPriority, DateTimeOffset, DateTimeOffset)> GenerateJobs(
        int count)
    {
        var rng  = new Random(42); // fixed seed for reproducibility
        var now  = DateTimeOffset.UtcNow;
        var jobs = new List<(Guid, JobPriority, DateTimeOffset, DateTimeOffset)>(count);

        var priorities = Enum.GetValues<JobPriority>();

        for (var i = 0; i < count; i++)
        {
            var scheduledAt = now.AddSeconds(rng.Next(0, 3600));
            var createdAt   = now.AddSeconds(-rng.Next(0, 600));
            var priority    = priorities[rng.Next(priorities.Length)];
            jobs.Add((Guid.NewGuid(), priority, scheduledAt, createdAt));
        }

        return jobs;
    }
}