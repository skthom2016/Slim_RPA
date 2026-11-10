using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Configuration;
using TractoRuntime.Browser.Diagnostics;
using TractoRuntime.Browser.Host;

// Settings come from a .env file beside the executable, then one in the folder the worker was
// started from; TRACTO_ENV_FILE names a different file instead. This is where a
// ${secret:NAME} reference finds TRACTO_SECRET_NAME, so it must happen before any job runs.
// A real environment variable always wins over the file.
var envFiles = Environment.GetEnvironmentVariable("TRACTO_ENV_FILE") is { Length: > 0 } named
    ? EnvFile.LoadAll(named)
    : EnvFile.LoadAll(Path.Combine(AppContext.BaseDirectory, ".env"), ".env");

foreach (var file in envFiles)
    Console.WriteLine($"[Browser Host] Settings loaded from {file}");

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

// Warnings and errors go to the console for whoever is watching, and to the sink, which sends
// them to the Orchestrator so the run can be explained after the window is closed.
var runLogSink = new RunLogSink();

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine      = true;
    o.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.AddProvider(new RunLogForwarder(runLogSink));

Console.WriteLine($"[Browser Host] Orchestrator: {options.OrchestratorUrl}  Worker: {options.WorkerId}  Mode: {options.Mode}"
    + (string.IsNullOrWhiteSpace(options.Profile) ? "" : $"  Profile: {options.Profile}"));

var playwright = await Playwright.CreateAsync();

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(playwright);
builder.Services.AddSingleton(runLogSink);
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
