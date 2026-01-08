using Hebner.Agent.Service;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Runs as Windows Service when installed, console in dev
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Hebner Remote Agent Service";
});

// Logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "service-.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);

// Dev broker stub (replace with Base44 broker later)
builder.Services.AddHttpClient("broker", c =>
{
    c.BaseAddress = new Uri("http://127.0.0.1:5189/");
    c.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<IpcService>();

var host = builder.Build();
await host.RunAsync();
