using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Assertion;

/// <summary>
/// Evaluates technical DOM assertions for <c>browser.page.assert</c> (DOM-008 / BRW-US-010 AC#1).
///
/// Only the five technical predicates defined by <see cref="AssertionKind"/> are evaluated:
/// visible, enabled, checked, URL equality, and exact displayed-text equality.  No domain
/// comparison, arithmetic, or business logic is performed (AC#2, AC#4).
/// </summary>
public interface IDomAssertionService
{
    /// <summary>
    /// Evaluates the supplied <see cref="AssertionSpec"/> against the current page state.
    /// </summary>
    /// <param name="page">The current Playwright page.</param>
    /// <param name="spec">The technical assertion to evaluate.</param>
    /// <param name="selector">
    /// The locator for element-scoped assertions (Visible, Enabled, Checked, TextEquals).
    /// May be null for URL-equality assertions.
    /// </param>
    /// <param name="timeoutMs">Per-step bounded timeout in milliseconds (BRE-001).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="AssertionResult"/> indicating whether the assertion passed or failed,
    /// with a structured error if it did not.
    /// </returns>
    Task<AssertionResult> EvaluateAsync(
        IPage page,
        AssertionSpec spec,
        SelectorRef? selector,
        float timeoutMs,
        CancellationToken ct);
}

/// <summary>Result of evaluating a technical DOM assertion.</summary>
public sealed record AssertionResult
{
    /// <summary>Whether the assertion predicate held true for the current page state.</summary>
    public required bool Passed { get; init; }

    /// <summary>Human-readable failure explanation (non-null when <see cref="Passed"/> is false).</summary>
    public string? FailureDetail { get; init; }

    /// <summary>CEC §6.2 error category for the failure (null when <see cref="Passed"/> is true).</summary>
    public ErrorCategory? ErrorCategory { get; init; }

    /// <summary>Namespaced <c>browser.*</c> detail code (null when <see cref="Passed"/> is true).</summary>
    public string? DetailCode { get; init; }

    /// <summary>Returns a passing assertion result.</summary>
    public static AssertionResult Pass() => new() { Passed = true };

    /// <summary>Returns a failing assertion result with the given structured error.</summary>
    public static AssertionResult Fail(string detail, ErrorCategory category, string detailCode)
        => new()
        {
            Passed        = false,
            FailureDetail = detail,
            ErrorCategory = category,
            DetailCode    = detailCode
        };
}
