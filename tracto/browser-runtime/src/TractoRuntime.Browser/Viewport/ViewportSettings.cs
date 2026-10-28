namespace TractoRuntime.Browser.Viewport;

/// <summary>
/// Declared viewport configuration for deterministic, reproducible DOM behavior (BRW-007).
/// A supported viewport must be in the certified list; unsupported viewports are rejected
/// before any browser interaction.
/// </summary>
public sealed record ViewportSettings
{
    /// <summary>Viewport width in CSS pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Viewport height in CSS pixels.</summary>
    public required int Height { get; init; }

    /// <summary>Device pixel ratio (device-scale-factor). Must match a certified entry.</summary>
    public double DeviceScaleFactor { get; init; } = 1.0;

    /// <summary>Default certified viewport: 1280x720 at 1x device-scale-factor.</summary>
    public static readonly ViewportSettings Default = new() { Width = 1280, Height = 720, DeviceScaleFactor = 1.0 };
}
