using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Host;

var builder = Host.CreateApplicationBuilder(args);

var options = BrowserHostOptions.FromConfiguration(builder.Configuration);

// `--login <profile> [url]` signs a profile in once; later runs that name it start signed in.
if (args.Length > 0 && args[0].Equals("--login", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: --login <profile-name> [start-url]");
        return 2;
    }
    return await ProfileLogin.RunAsync(options, args[1], args.Length > 2 ? args[2] : null);
}

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine      = true;
    o.TimestampFormat = "HH:mm:ss ";
});

Console.WriteLine($"[Browser Host] Orchestrator: {options.OrchestratorUrl}  Worker: {options.WorkerId}  Mode: {options.Mode}"
    + (string.IsNullOrWhiteSpace(options.Profile) ? "" : $"  Profile: {options.Profile}"));

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
return 0;
