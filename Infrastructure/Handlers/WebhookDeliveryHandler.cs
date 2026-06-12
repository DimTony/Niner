using System.Text;
using System.Text.Json;
using Core.DTOs;
using Core.Interfaces;
using Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Infrastructure.Handlers;

public class WebhookDeliveryHandler : IJobHandler
{
    private readonly ILogger<WebhookDeliveryHandler> _logger;
    private static readonly Random _rng = new();
    private readonly double _failureRate;

    // Allowed methods
    private static readonly HashSet<string> ValidMethods =
        ["GET", "POST", "PUT", "PATCH", "DELETE"];

    public WebhookDeliveryHandler(
    ILogger<WebhookDeliveryHandler> logger,
    IOptions<HandlerOptions> options)
    {
        _logger = logger;
        _failureRate = options.Value.WebhookDeliveryFailureRate;
    }

    public async Task<JobHandlerResult> Execute(
        string payload,
        CancellationToken ct = default)
    {
        WebhookDeliveryPayload webhook;

        try
        {
            webhook = JsonSerializer.Deserialize<WebhookDeliveryPayload>(
                payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Payload deserialized to null.");
        }
        catch (Exception ex)
        {
            return new JobHandlerResult(false, $"Invalid payload: {ex.Message}");
        }

        // Validate
        if (string.IsNullOrWhiteSpace(webhook.Url))
            return new JobHandlerResult(false, "Missing required field: url");

        if (!Uri.TryCreate(webhook.Url, UriKind.Absolute, out _))
            return new JobHandlerResult(false, $"Invalid URL: {webhook.Url}");

        var method = (webhook.Method ?? "POST").ToUpperInvariant();
        if (!ValidMethods.Contains(method))
            return new JobHandlerResult(false, $"Unsupported HTTP method: {method}");

        // Simulate network latency
        await Task.Delay(TimeSpan.FromMilliseconds(_rng.Next(3000, 5000)), ct);

        // Simulate transient failures
        if (_rng.NextDouble() < _failureRate)
        {
            var httpError = _rng.Next(4) switch
            {
                0 => "HTTP 503: Service unavailable",
                1 => "HTTP 429: Too many requests",
                2 => "HTTP 502: Bad gateway",
                _ => "Connection refused: endpoint unreachable"
            };

            _logger.LogWarning(
                "WebhookDeliveryHandler: delivery failed. Url={Url} Method={Method} Error={Error}",
                webhook.Url, method, httpError);

            return new JobHandlerResult(false, httpError);
        }

        // Build simulated request summary
        var headers = webhook.Headers ?? new Dictionary<string, string>();
        headers.TryAdd("Content-Type", "application/json");
        headers.TryAdd("X-Webhook-Id", Guid.NewGuid().ToString("N"));

        _logger.LogInformation(
            "WebhookDeliveryHandler: delivered. Url={Url} Method={Method} Headers={HeaderCount} BodyLength={BodyLength}",
            webhook.Url,
            method,
            headers.Count,
            webhook.Body?.Length ?? 0);

        return new JobHandlerResult(
            Success: true,
            ResultData: new
            {
                requestId = headers["X-Webhook-Id"],
                deliveredAt = DateTimeOffset.UtcNow,
                url = webhook.Url,
                method,
                statusCode = 200
            });
    }
}
