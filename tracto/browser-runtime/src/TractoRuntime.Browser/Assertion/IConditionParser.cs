using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Assertion;

/// <summary>
/// Validates a technical assertion predicate before any browser interaction (DOM-008 /
/// BRW-US-010 AC#2 / BRW-AT-14).
///
/// Accepts the five technical predicates defined by <see cref="AssertionKind"/> and rejects:
/// <list type="bullet">
///   <item>Raw expression strings (always a business predicate).</item>
///   <item>Expected-value strings containing numeric comparison or arithmetic operators.</item>
///   <item>Missing expected values for kinds that require them.</item>
/// </list>
/// </summary>
public interface IConditionParser
{
    /// <summary>
    /// Validates the supplied <see cref="AssertionSpec"/> and returns a
    /// <see cref="ConditionParseResult"/> indicating whether the predicate is a permitted
    /// technical assertion or a rejected business expression.
    /// </summary>
    ConditionParseResult Validate(AssertionSpec spec);
}

/// <summary>Result of a condition parse/validation operation.</summary>
public sealed record ConditionParseResult
{
    /// <summary>Whether the assertion predicate is a valid technical predicate.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Human-readable rejection reason (non-null when <see cref="IsValid"/> is false).</summary>
    public string? RejectionReason { get; init; }

    /// <summary>Namespaced <c>browser.*</c> detail code for rejection diagnostics.</summary>
    public string? DetailCode { get; init; }

    /// <summary>Returns a result indicating the predicate is a valid technical assertion.</summary>
    public static ConditionParseResult Valid() => new() { IsValid = true };

    /// <summary>Returns a result indicating the predicate was rejected.</summary>
    public static ConditionParseResult Rejected(string reason, string detailCode)
        => new() { IsValid = false, RejectionReason = reason, DetailCode = detailCode };
}
