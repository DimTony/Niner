using Core.Interfaces;
using Infrastructure.Handlers;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Infrastructure.Redis;
using Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Postgres")));

        var redisConn = configuration.GetConnectionString("Redis")!;
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConn));

        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IDeadLetterRepository, DeadLetterRepository>();
        services.AddScoped<IJobLogRepository, JobLogRepository>();

        services.AddSingleton<IJobQueueService, JobQueueService>();
        services.AddSingleton<IEventPublisher, EventPublisher>();

        services.AddTransient<SendEmailHandler>();
        services.AddTransient<WebhookDeliveryHandler>();
        services.AddTransient<LogProcessorHandler>();
        services.AddSingleton<IJobHandlerFactory, JobHandlerFactory>();

        services.AddSingleton<IAlertService, AlertService>();

        return services;
    }
}