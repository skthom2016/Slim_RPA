using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Packaging;

/// <summary>
/// Thrown by <see cref="IOfflineBundleValidator"/> when the offline bundle is incomplete or
/// structurally invalid — for example, a missing browser binary or absent Playwright version
/// reference (BRW-US-021 AC#2, AC#3 / BRW-AT-19).
///
/// Maps to CEC v1.0.1 <c>errorCategory=ENVIRONMENT</c> with
/// <c>detailCode=browser.notInstalled</c>, matching the existing convention established by
/// <c>BinaryValidationException</c>.
/// </summary>
public sealed class OfflineBundleException : Exception
{
    /// <summary>CEC error category — always <see cref="ErrorCategory.ENVIRONMENT"/>.</summary>
    public ErrorCategory Category { get; } = ErrorCategory.ENVIRONMENT;

    /// <summary>CEC namespaced detail code.</summary>
    public string DetailCode { get; } = "browser.notInstalled";

    public OfflineBundleException(string message)
        : base(message) { }

    public OfflineBundleException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Converts this exception to the CEC <see cref="CecError"/> wire record.</summary>
    public CecError ToCecError() => new()
    {
        Category   = Category,
        Message    = Message,
        DetailCode = DetailCode
    };
}
