using Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

public class AlertService : IAlertService
{
    private readonly ILogger<AlertService> _logger;

    public AlertService(ILogger<AlertService> logger)
    {
        _logger = logger;
    }

    public Task SendDlqThresholdAlert(int count, CancellationToken ct = default)
    {
        // Mocked — in production this would call SendGrid, SES, etc.
        _logger.LogWarning(
            "ALERT [DLQ_THRESHOLD_EXCEEDED] " +
            "UnresolvedCount={Count} " +
            "Threshold={Threshold} " +
            "Action=EmailAlertSimulated " +
            "To=ops@jobscheduler.io " +
            "Subject='Dead Letter Queue threshold exceeded' " +
            "Timestamp={Timestamp}",
            count,
            count,
            DateTimeOffset.UtcNow);

        return Task.CompletedTask;
    }
}