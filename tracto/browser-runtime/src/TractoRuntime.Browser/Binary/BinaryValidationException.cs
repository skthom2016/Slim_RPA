using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Binary;

/// <summary>
/// Thrown by <see cref="BinaryValidator"/> when a Playwright browser binary is missing,
/// has an unexpected size, or does not match the pinned SHA-256 hash.
/// Carries the exact CEC v1.0.1 error envelope so callers can surface it without
/// further translation.
/// </summary>
public sealed class BinaryValidationException : Exception
{
    public ErrorCategory Category { get; } = ErrorCategory.ENVIRONMENT;
    public string DetailCode { get; }

    public BinaryValidationException(string message, string detailCode = "browser.notInstalled")
        : base(message)
    {
        DetailCode = detailCode;
    }

    public BinaryValidationException(string message, string detailCode, Exception innerException)
        : base(message, innerException)
    {
        DetailCode = detailCode;
    }

    /// <summary>Converts this exception to the CEC <see cref="CecError"/> wire record.</summary>
    public CecError ToCecError() => new()
    {
        Category  = Category,
        Message   = Message,
        DetailCode = DetailCode
    };
}
