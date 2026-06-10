namespace Core.DTOs;

public record WebhookDeliveryPayload(
    string Url,
    string Method,
    Dictionary<string, string>? Headers,
    string? Body);