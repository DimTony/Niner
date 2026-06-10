using Core.Enums;
using Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Handlers;

public class JobHandlerFactory : IJobHandlerFactory
{
    private readonly IServiceProvider _services;

    public JobHandlerFactory(IServiceProvider services)
    {
        _services = services;
    }

    public IJobHandler Resolve(JobType type)
    {
        return type switch
        {
            JobType.SendEmail        => _services.GetRequiredService<SendEmailHandler>(),
            JobType.WebhookDelivery  => _services.GetRequiredService<WebhookDeliveryHandler>(),
            JobType.LogProcessor     => _services.GetRequiredService<LogProcessorHandler>(),
            _ => throw new InvalidOperationException($"No handler registered for job type: {type}")
        };
    }
}