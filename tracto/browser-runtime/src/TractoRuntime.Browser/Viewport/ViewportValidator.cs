namespace TractoRuntime.Browser.Viewport;

/// <summary>
/// Validates viewport settings against the certified list (BRW-007).
/// Unsupported viewports are rejected with <see cref="ViewportNotSupportedException"/>
/// (errorCategory=VALIDATION) before any browser interaction.
/// </summary>
public sealed class ViewportValidator : IViewportValidator
{
    // Certified viewport configurations for repeatable DOM behavior.
    // Only these combinations of width, height, and device-scale-factor are approved.
    private static readonly ViewportSettings[] _certified =
    [
        new() { Width = 1280, Height =  720, DeviceScaleFactor = 1.0 },
        new() { Width = 1366, Height =  768, DeviceScaleFactor = 1.0 },
        new() { Width = 1440, Height =  900, DeviceScaleFactor = 1.0 },
        new() { Width = 1920, Height = 1080, DeviceScaleFactor = 1.0 },
        new() { Width = 1280, Height =  800, DeviceScaleFactor = 2.0 },  // HiDPI
        new() { Width = 1920, Height = 1080, DeviceScaleFactor = 2.0 },  // HiDPI
    ];

    private const double ScaleTolerance = 0.001;

    /// <inheritdoc/>
    public IReadOnlyList<ViewportSettings> GetCertifiedViewports() => _certified;

    /// <inheritdoc/>
    public Task ValidateAsync(ViewportSettings viewport, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var supported = Array.Exists(_certified, c =>
            c.Width  == viewport.Width &&
            c.Height == viewport.Height &&
            Math.Abs(c.DeviceScaleFactor - viewport.DeviceScaleFactor) < ScaleTolerance);

        if (!supported)
            throw new ViewportNotSupportedException(viewport, _certified);

        return Task.CompletedTask;
    }
}
