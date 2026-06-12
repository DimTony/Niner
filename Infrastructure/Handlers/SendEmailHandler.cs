using System.Text.Json;
using Core.Interfaces;
using Core.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Core.Options;

namespace Infrastructure.Handlers;

public class SendEmailHandler : IJobHandler
{
    private readonly ILogger<SendEmailHandler> _logger;
    private static readonly Random _rng = new();
    private readonly double _failureRate;

    public SendEmailHandler(ILogger<SendEmailHandler> logger, IOptions<HandlerOptions> options)
    {
        _logger = logger;
         _failureRate = options.Value.SendEmailFailureRate;
    }

    public async Task<JobHandlerResult> Execute(
        string payload,
        CancellationToken ct = default)
    {
        SendEmailPayload email;

        try
        {
            email = JsonSerializer.Deserialize<SendEmailPayload>(
                payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Payload deserialized to null.");
        }
        catch (Exception ex)
        {
            return new JobHandlerResult(
                Success: false,
                ErrorMessage: $"Invalid payload: {ex.Message}");
        }

        // Validate required fields
        if (string.IsNullOrWhiteSpace(email.To))
            return new JobHandlerResult(false, "Missing required field: to");

        if (string.IsNullOrWhiteSpace(email.Subject))
            return new JobHandlerResult(false, "Missing required field: subject");

        if (!email.To.Contains('@'))
            return new JobHandlerResult(false, $"Invalid email address: {email.To}");

        // Simulate SMTP handshake delay
        await Task.Delay(TimeSpan.FromMilliseconds(_rng.Next(60000, 70000)), ct);

        // Simulate transient SMTP failure
        if (_rng.NextDouble() < _failureRate)
        {
            var smtpError = _rng.Next(3) switch
            {
                0 => "SMTP 421: Service temporarily unavailable",
                1 => "SMTP 450: Mailbox temporarily unavailable",
                _ => "SMTP 550: Connection timeout"
            };

            _logger.LogWarning(
                "SendEmailHandler: simulated SMTP failure. To={To} Subject={Subject} Error={Error}",
                email.To, email.Subject, smtpError);

            return new JobHandlerResult(false, smtpError);
        }

        // Success — log what would have been sent
        _logger.LogInformation(
            "SendEmailHandler: email delivered. From={From} To={To} Subject={Subject} BodyLength={BodyLength}",
            email.From ?? "noreply@jobscheduler.io",
            email.To,
            email.Subject,
            email.Body?.Length ?? 0);

        return new JobHandlerResult(
            Success: true,
            ResultData: new
            {
                messageId = $"msg_{Guid.NewGuid():N}",
                deliveredAt = DateTimeOffset.UtcNow,
                to = email.To,
                subject = email.Subject
            });
    }
}
