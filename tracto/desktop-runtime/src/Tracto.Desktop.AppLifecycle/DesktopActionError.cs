namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Exception raised when a desktop automation action cannot be completed.
/// Maps to CEC v1.0.1 error categories and desktop-specific detail codes.
/// </summary>
public sealed class DesktopActionError : Exception
{
    /// <summary>CEC v1.0.1 errorCategory string, e.g. "ENVIRONMENT", "TARGET_NOT_FOUND".</summary>
    public string ErrorCategory { get; }

    /// <summary>Optional desktop-specific detailCode, e.g. "desktop.appCrash".</summary>
    public string? DetailCode { get; }

    /// <summary>Human-readable description of the error; must not contain secret values.</summary>
    public string Description { get; }

    public DesktopActionError(string errorCategory, string? detailCode, string description)
        : base($"[{errorCategory}] {(detailCode is not null ? detailCode + ": " : string.Empty)}{description}")
    {
        ErrorCategory = errorCategory ?? throw new ArgumentNullException(nameof(errorCategory));
        DetailCode = detailCode;
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    public DesktopActionError(string errorCategory, string? detailCode, string description, Exception inner)
        : base($"[{errorCategory}] {(detailCode is not null ? detailCode + ": " : string.Empty)}{description}", inner)
    {
        ErrorCategory = errorCategory ?? throw new ArgumentNullException(nameof(errorCategory));
        DetailCode = detailCode;
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }
}
