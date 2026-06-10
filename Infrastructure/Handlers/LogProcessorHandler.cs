using System.Text.Json;
using Core.Interfaces;
using Core.DTOs;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Infrastructure.Handlers;

public class LogProcessorHandler : IJobHandler
{
    private readonly ILogger<LogProcessorHandler> _logger;
    private static readonly Random _rng = new();

    private static readonly HashSet<string> ValidLevels =
        ["DEBUG", "INFO", "WARN", "ERROR", "FATAL"];

    // Simulated failure rate — 15%
    private const double FailureRate = 0.15;

    public LogProcessorHandler(ILogger<LogProcessorHandler> logger)
    {
        _logger = logger;
    }

    public async Task<JobHandlerResult> Execute(
        string payload,
        CancellationToken ct = default)
    {
        LogProcessorPayload logEntry;

        try
        {
            logEntry = JsonSerializer.Deserialize<LogProcessorPayload>(
                payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Payload deserialized to null.");
        }
        catch (Exception ex)
        {
            return new JobHandlerResult(false, $"Invalid payload: {ex.Message}");
        }

        // Validate
        if (string.IsNullOrWhiteSpace(logEntry.Source))
            return new JobHandlerResult(false, "Missing required field: source");

        if (string.IsNullOrWhiteSpace(logEntry.Message))
            return new JobHandlerResult(false, "Missing required field: message");

        var level = (logEntry.Level ?? "INFO").ToUpperInvariant();
        if (!ValidLevels.Contains(level))
            return new JobHandlerResult(false, $"Invalid log level: {level}");

        // Simulate processing time — parsing, enriching, routing
        await Task.Delay(TimeSpan.FromMilliseconds(_rng.Next(50, 300)), ct);

        // Simulate storage write failure
        if (_rng.NextDouble() < FailureRate)
        {
            const string storageError = "Log storage write failed: buffer overflow";

            _logger.LogWarning(
                "LogProcessorHandler: processing failed. Source={Source} Level={Level} Error={Error}",
                logEntry.Source, level, storageError);

            return new JobHandlerResult(false, storageError);
        }

        // Enrich the log entry with computed fields
        var enriched = new
        {
            entryId   = Guid.NewGuid().ToString("N"),
            source    = logEntry.Source,
            level,
            message   = logEntry.Message,
            fields    = logEntry.Fields ?? new Dictionary<string, string>(),
            processedAt = DateTimeOffset.UtcNow,
            fingerprint = ComputeFingerprint(logEntry.Source, logEntry.Message)
        };

        _logger.LogInformation(
            "LogProcessorHandler: entry processed. Source={Source} Level={Level} EntryId={EntryId} Fingerprint={Fingerprint}",
            logEntry.Source, level, enriched.entryId, enriched.fingerprint);

        return new JobHandlerResult(Success: true, ResultData: enriched);
    }

    // Stable hash for deduplication fingerprint
    private static string ComputeFingerprint(string source, string message)
    {
        var raw = $"{source}:{message}";
        var bytes = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}