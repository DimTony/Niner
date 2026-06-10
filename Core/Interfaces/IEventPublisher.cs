namespace Core.Interfaces;

public interface IEventPublisher
{
    Task PublishJobEvent(Guid jobId, string status, CancellationToken ct = default);
}