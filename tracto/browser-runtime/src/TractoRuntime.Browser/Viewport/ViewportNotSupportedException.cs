using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Viewport;

/// <summary>
/// Thrown when the artifact declares a viewport that is not in the certified list (BRW-007).
/// Maps to <see cref="ErrorCategory.VALIDATION"/> with detail code
/// <c>browser.unsupportedViewport</c> and must be raised before any browser interaction.
/// </summary>
public sealed class ViewportNotSupportedException : Exception
{
    /// <summary>The viewport requested in the artifact that is not supported.</summary>
    public ViewportSettings RequestedViewport { get; }

    /// <summary>The set of certified, pre-approved viewports the runtime accepts.</summary>
    public IReadOnlyList<ViewportSettings> CertifiedViewports { get; }

    /// <summary>CEC error category — always VALIDATION for this exception.</summary>
    public ErrorCategory ErrorCategory => ErrorCategory.VALIDATION;

    /// <summary>CEC detail code for unsupported viewport.</summary>
    public string DetailCode => "browser.unsupportedViewport";

    public ViewportNotSupportedException(
        ViewportSettings requested,
        IReadOnlyList<ViewportSettings> certified)
        : base($"Viewport {requested.Width}x{requested.Height} @ {requested.DeviceScaleFactor}x " +
               "is not in the certified viewport list. " +
               "Declare a supported viewport in the artifact or omit the field to use the default.")
    {
        RequestedViewport = requested;
        CertifiedViewports = certified;
    }
}
