using Mise.Api.Infrastructure;
using Mise.Application;
using Mise.Infrastructure;
using Mise.Infrastructure.Push;
using Serilog;

// Служебная команда: пара ключей VAPID для пушей — напечатать и выйти (сервер не запускается).
if (args.Contains("--generate-vapid-keys"))
{
    Console.Write(VapidKeyGenerator.Describe());
    return;
}

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, logger) => logger
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services
        .AddApplication()
        .AddInfrastructure(builder.Configuration)
        .AddApiServices(builder.Configuration);

    var app = builder.Build();

    app.UseApiPipeline();
    app.MapApiEndpoints();

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Mise.Api terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
