namespace Core.Interfaces;

public interface IAlertService
{
    Task SendDlqThresholdAlert(int count, CancellationToken ct = default);
}