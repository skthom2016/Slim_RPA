using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Locators;

/// <summary>
/// Thrown by <see cref="LocatorResolver"/> when no declared locator strategy matches
/// any element in the current DOM / frame scope (DOM-001 / BRW-US-007 AC-4).
/// Maps to <see cref="ErrorCategory.TARGET_NOT_FOUND"/> / <c>browser.locatorNotFound</c>.
/// </summary>
public sealed class LocatorNotFoundException : Exception
{
    public ErrorCategory Category { get; } = ErrorCategory.TARGET_NOT_FOUND;
    public string DetailCode { get; }

    public LocatorNotFoundException(string message,
        string detailCode = "browser.locatorNotFound")
        : base(message)
    {
        DetailCode = detailCode;
    }

    public LocatorNotFoundException(string message, string detailCode, Exception innerException)
        : base(message, innerException)
    {
        DetailCode = detailCode;
    }

    /// <summary>Converts this exception to the CEC <see cref="CecError"/> wire record.</summary>
    public CecError ToCecError() => new()
    {
        Category   = Category,
        Message    = Message,
        DetailCode = DetailCode
    };
}
