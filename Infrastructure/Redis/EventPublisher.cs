using System.Text.Json;
using Core.Interfaces;
using StackExchange.Redis;

namespace Infrastructure.Redis;

public class EventPublisher : IEventPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private const string Channel = "job:events";

    public EventPublisher(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task PublishJobEvent(
        Guid jobId,
        string status,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            jobId = jobId.ToString(),
            status,
            timestamp = DateTimeOffset.UtcNow
        });

        var subscriber = _redis.GetSubscriber();
        await subscriber.PublishAsync(
            RedisChannel.Literal(Channel),
            payload);
    }
}