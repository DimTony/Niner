namespace Core.DTOs;

public record SendEmailPayload(
    string To,
    string Subject,
    string Body,
    string? From = null);