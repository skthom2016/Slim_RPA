using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Host;

var builder = Host.CreateApplicationBuilder(args);

var options = BrowserHostOptions.FromConfiguration(builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine      = true;
    o.TimestampFormat = "HH:mm:ss ";
});

Console.WriteLine($"[Browser Host] Orchestrator: {options.OrchestratorUrl}  Worker: {options.WorkerId}  Mode: {options.Mode}");

var playwright = await Playwright.CreateAsync();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(playwright);
builder.Services.AddHttpClient<OrchestratorClient>(client =>
{
    client.BaseAddress = new Uri(options.OrchestratorUrl.TrimEnd('/') + "/");
    client.Timeout     = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<BrowserRunEngine>();
builder.Services.AddHostedService<BrowserWorker>();

using var host = builder.Build();
await host.RunAsync();

playwright.Dispose();
