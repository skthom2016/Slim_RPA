namespace Tracto.Desktop.Element;

/// <summary>
/// Exception raised by element-finder operations to convey structured error codes
/// to the step executor (TARGET_NOT_FOUND, TARGET_AMBIGUOUS, etc.).
/// </summary>
public sealed class DesktopActionError : Exception
{
    public DesktopActionError(string errorCode)
        : base($"Desktop action failed: {errorCode}")
    {
        ErrorCode = errorCode;
        ErrorCategory = errorCode;
    }

    public DesktopActionError(string errorCode, string detail)
        : base($"Desktop action failed: {errorCode} — {detail}")
    {
        ErrorCode = errorCode;
        ErrorCategory = errorCode;
        DetailCode = detail;
    }

    public DesktopActionError(string errorCode, string detail, Exception inner)
        : base($"Desktop action failed: {errorCode} — {detail}", inner)
    {
        ErrorCode = errorCode;
        ErrorCategory = errorCode;
        DetailCode = detail;
    }

    /// <summary>
    /// Machine-readable error code (CEC v1.0.1 errorCategory), e.g. "TARGET_NOT_FOUND", "ENVIRONMENT", "POLICY_DENIED".
    /// </summary>
    public string ErrorCode { get; }

    /// <summary>
    /// Alias for <see cref="ErrorCode"/> — the CEC v1.0.1 errorCategory value.
    /// </summary>
    public string ErrorCategory { get; }

    /// <summary>
    /// Desktop-specific detail code, e.g. "desktop.elementDisabled". Null when not set.
    /// </summary>
    public string? DetailCode { get; }
}
