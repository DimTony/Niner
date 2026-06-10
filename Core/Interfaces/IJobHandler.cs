using Core.Enums;

namespace Core.Interfaces;

public record JobHandlerResult(
    bool Success,
    string? ErrorMessage = null,
    object? ResultData = null);

public interface IJobHandler
{
    Task<JobHandlerResult> Execute(
        string payload,
        CancellationToken ct = default);
}

public interface IJobHandlerFactory
{
    IJobHandler Resolve(JobType type);
}