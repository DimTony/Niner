using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace Api.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<EventsController> _logger;

    public EventsController(
        IConnectionMultiplexer redis,
        ILogger<EventsController> logger)
    {
        _redis  = redis;
        _logger = logger;
    }

    /// <summary>
    /// SSE stream — subscribe to receive real-time job status events.
    /// Connect once; events arrive as server-sent events.
    /// </summary>
    [HttpGet("stream")]
    public async Task Stream(CancellationToken ct)
    {
        Response.Headers.Append("Content-Type",  "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection",    "keep-alive");
        Response.Headers.Append("X-Accel-Buffering", "no"); // disable Nginx buffering

        await Response.Body.FlushAsync(ct);

        var sub = _redis.GetSubscriber();
        var tcs = new TaskCompletionSource();

        // Write initial heartbeat so client knows the connection is live
        await WriteEventAsync("connected", "{\"message\":\"SSE stream connected\"}", ct);

        await sub.SubscribeAsync(
            RedisChannel.Literal("job:events"),
            async (_, message) =>
            {
                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetResult();
                    return;
                }

                try
                {
                    await WriteEventAsync("job_update", message!, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex, "SSE write failed — client likely disconnected.");
                    tcs.TrySetResult();
                }
            });

        // Keep connection open until client disconnects or server stops
        using var reg = ct.Register(() => tcs.TrySetResult());
        await tcs.Task;

        await sub.UnsubscribeAsync(RedisChannel.Literal("job:events"));
    }

    private async Task WriteEventAsync(
        string eventName,
        string data,
        CancellationToken ct)
    {
        var payload = $"event: {eventName}\ndata: {data}\n\n";
        await Response.Body.WriteAsync(
            System.Text.Encoding.UTF8.GetBytes(payload), ct);
        await Response.Body.FlushAsync(ct);
    }
}