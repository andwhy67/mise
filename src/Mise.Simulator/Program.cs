using Mise.Simulator;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<SimulatorWorker>();

var host = builder.Build();
await host.RunAsync();
