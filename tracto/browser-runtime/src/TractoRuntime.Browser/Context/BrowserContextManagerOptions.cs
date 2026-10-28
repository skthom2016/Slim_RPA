using TractoRuntime.Browser.Network;

namespace TractoRuntime.Browser.Context;

/// <summary>
/// Immutable configuration for <see cref="BrowserContextManager"/>.
/// Viewport settings are enforced per-context to ensure reproducibility (BRW-006).
/// Network policy settings are applied to every new context (BRW-005, BRW-008).
/// </summary>
public sealed record BrowserContextManagerOptions
{
    /// <summary>Playwright browser type key: "chromium", "firefox", or "webkit".</summary>
    public string BrowserType { get; init; } = "chromium";

    /// <summary>Fixed certified viewport width in CSS pixels.</summary>
    public int ViewportWidth { get; init; } = 1920;

    /// <summary>Fixed certified viewport height in CSS pixels.</summary>
    public int ViewportHeight { get; init; } = 1080;

    /// <summary>Device pixel ratio for the certified viewport.</summary>
    public float DeviceScaleFactor { get; init; } = 1.0f;

    /// <summary>Optional BCP 47 locale string (e.g. "en-US").  Null uses the system default.</summary>
    public string? Locale { get; init; }

    /// <summary>Optional IANA timezone ID (e.g. "America/New_York").  Null uses the system default.</summary>
    public string? TimezoneId { get; init; }

    /// <summary>
    /// Network policy applied to every browser context created by this manager.
    /// When set, the origin allowlist is enforced via route interception (BRW-US-005 AC#1/AC#2)
    /// and enterprise proxy settings are applied to context options (BRW-US-005 AC#5).
    /// When null, no origin enforcement or proxy configuration is applied.
    /// </summary>
    public NetworkPolicy? NetworkPolicy { get; init; }
}
