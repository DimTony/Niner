using Core.Options;
using Infrastructure;
using Scheduler;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.AddInfrastructure(ctx.Configuration);
        services.Configure<SchedulerOptions>(
            ctx.Configuration.GetSection(SchedulerOptions.Section));
        services.AddHostedService<SchedulerService>();
    })
    .Build();

await host.RunAsync();