using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Navigation;

/// <summary>
/// Result of a navigation operation executed by <see cref="INavigationExecutor"/>
/// (BRW-US-005).
/// </summary>
public sealed record NavigationResult
{
    /// <summary>True when navigation completed successfully and all assertions passed.</summary>
    public required bool Succeeded { get; init; }

    /// <summary>The page URL after navigation completed. Null on failure.</summary>
    public string? FinalUrl { get; init; }

    /// <summary>CEC error category when <see cref="Succeeded"/> is false.</summary>
    public ErrorCategory? ErrorCategory { get; init; }

    /// <summary>Browser-specific detail code when <see cref="Succeeded"/> is false.</summary>
    public string? DetailCode { get; init; }

    /// <summary>Human-readable error message (safe to log — no credentials or PII).</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Creates a successful navigation result with the given final URL.</summary>
    public static NavigationResult Success(string finalUrl)
        => new() { Succeeded = true, FinalUrl = finalUrl };

    /// <summary>Creates a failed navigation result with a CEC-conformant error.</summary>
    public static NavigationResult Failure(
        ErrorCategory category,
        string detailCode,
        string message)
        => new()
        {
            Succeeded = false,
            ErrorCategory = category,
            DetailCode = detailCode,
            ErrorMessage = message
        };
}
