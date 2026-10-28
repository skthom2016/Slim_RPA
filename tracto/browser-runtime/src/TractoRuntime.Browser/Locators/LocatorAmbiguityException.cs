using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Locators;

/// <summary>
/// Thrown by <see cref="LocatorResolver"/> when a locator strategy matches more than one element
/// without scoped disambiguation (DOM-002 / BRW-US-007 AC-2).
/// Maps to <see cref="ErrorCategory.TARGET_AMBIGUOUS"/> / <c>browser.multipleLocatorMatches</c>.
/// </summary>
public sealed class LocatorAmbiguityException : Exception
{
    public ErrorCategory Category { get; } = ErrorCategory.TARGET_AMBIGUOUS;
    public string DetailCode { get; }

    public LocatorAmbiguityException(string message,
        string detailCode = "browser.multipleLocatorMatches")
        : base(message)
    {
        DetailCode = detailCode;
    }

    public LocatorAmbiguityException(string message, string detailCode, Exception innerException)
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
