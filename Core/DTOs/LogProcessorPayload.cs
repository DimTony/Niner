namespace Core.DTOs;

public record LogProcessorPayload(
    string Source,
    string Level,
    string Message,
    Dictionary<string, string>? Fields);