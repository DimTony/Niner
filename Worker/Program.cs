using Worker;
using Core.Options;
using Infrastructure;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        services.AddInfrastructure(ctx.Configuration);
        services.Configure<WorkerOptions>(
            ctx.Configuration.GetSection(WorkerOptions.Section));
        services.AddHostedService<WorkerService>();
    })
    .Build();

await host.RunAsync();