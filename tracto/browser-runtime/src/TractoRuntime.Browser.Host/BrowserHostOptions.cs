using Microsoft.Extensions.Configuration;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Host;

/// <summary>
/// Host settings. Each value is read from an environment variable first, then from the
/// <c>Host</c> section of <c>appsettings.json</c>, then falls back to the default.
/// </summary>
public sealed record BrowserHostOptions
{
    public string OrchestratorUrl { get; init; } = "http://localhost:5080";
    public string WorkerId { get; init; } = "browser-runtime-001";
    public string WorkerVersion { get; init; } = "1.0.0";

    /// <summary><c>headed</c> or <c>headless</c>.</summary>
    public string Mode { get; init; } = "headed";

    /// <summary>Empty for the Playwright-bundled Chromium; <c>chrome</c> or <c>msedge</c> for an installed browser.</summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>Install the bundled Chromium on start-up when it is missing (bundled channel only).</summary>
    public bool AutoInstallBrowser { get; init; } = true;

    public int ViewportWidth { get; init; } = 1280;
    public int ViewportHeight { get; init; } = 720;
    public int PollIntervalMs { get; init; } = 5_000;
    public int HeartbeatIntervalMs { get; init; } = 15_000;
    public int RunTimeoutMs { get; init; } = 600_000;

    /// <summary>Time the browser stays open after the last step so a person can see the end state (headed only).</summary>
    public int PauseAfterRunMs { get; init; } = 3_000;

    /// <summary>How long a step waits for its target element to appear before reporting it as not found.</summary>
    public int LocatorWaitMs { get; init; } = 15_000;

    /// <summary>Allow <c>css</c> selectors in scripts.</summary>
    public bool AllowCss { get; init; } = true;

    public string? InteractiveSessionId { get; init; }

    public BrowserMode BrowserMode =>
        Mode.Equals("headless", StringComparison.OrdinalIgnoreCase) ? BrowserMode.Headless : BrowserMode.Headed;

    public static BrowserHostOptions FromConfiguration(IConfiguration cfg)
    {
        var d = new BrowserHostOptions();
        return new BrowserHostOptions
        {
            OrchestratorUrl     = Str("ORCHESTRATOR_URL",   "OrchestratorUrl",     d.OrchestratorUrl),
            WorkerId            = Str("WORKER_ID",          "WorkerId",            d.WorkerId),
            WorkerVersion       = Str("WORKER_VERSION",     "WorkerVersion",       d.WorkerVersion),
            Mode                = Str("BROWSER_MODE",       "Mode",                d.Mode),
            Channel             = Str("BROWSER_CHANNEL",    "Channel",             d.Channel),
            AutoInstallBrowser  = Bool("BROWSER_AUTO_INSTALL", "AutoInstallBrowser", d.AutoInstallBrowser),
            ViewportWidth       = Int("BROWSER_VIEWPORT_WIDTH",  "ViewportWidth",  d.ViewportWidth),
            ViewportHeight      = Int("BROWSER_VIEWPORT_HEIGHT", "ViewportHeight", d.ViewportHeight),
            PollIntervalMs      = Int("POLL_INTERVAL_MS",   "PollIntervalMs",      d.PollIntervalMs),
            HeartbeatIntervalMs = Int("HEARTBEAT_INTERVAL_MS", "HeartbeatIntervalMs", d.HeartbeatIntervalMs),
            RunTimeoutMs        = Int("RUN_TIMEOUT_MS",     "RunTimeoutMs",        d.RunTimeoutMs),
            PauseAfterRunMs     = Int("PAUSE_AFTER_RUN_MS", "PauseAfterRunMs",     d.PauseAfterRunMs),
            LocatorWaitMs       = Int("LOCATOR_WAIT_MS",    "LocatorWaitMs",       d.LocatorWaitMs),
            AllowCss            = Bool("BROWSER_ALLOW_CSS", "AllowCss",            d.AllowCss),
            InteractiveSessionId = System.Environment.GetEnvironmentVariable("INTERACTIVE_SESSION_ID")
                                   ?? System.Environment.GetEnvironmentVariable("SESSIONNAME")
                                   ?? "Console"
        };

        string Str(string env, string key, string fallback) =>
            System.Environment.GetEnvironmentVariable(env) ?? cfg[$"Host:{key}"] ?? fallback;

        int Int(string env, string key, int fallback) =>
            int.TryParse(Str(env, key, fallback.ToString()), out var v) ? v : fallback;

        bool Bool(string env, string key, bool fallback) =>
            bool.TryParse(Str(env, key, fallback.ToString()), out var v) ? v : fallback;
    }
}
