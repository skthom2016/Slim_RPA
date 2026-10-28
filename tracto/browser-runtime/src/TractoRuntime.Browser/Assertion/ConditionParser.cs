using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Assertion;

/// <summary>
/// Default implementation of <see cref="IConditionParser"/>.
///
/// Rejects an <see cref="AssertionSpec"/> when:
/// <list type="bullet">
///   <item><see cref="AssertionSpec.RawExpression"/> is non-null — always treated as a
///         business or arithmetic predicate (BRW-AT-14).</item>
///   <item><see cref="AssertionSpec.ExpectedValue"/> contains a comparison operator token
///         (<c>&gt;</c>, <c>&lt;</c>, <c>&gt;=</c>, <c>&lt;=</c>, <c>!=</c>) that indicates
///         a numeric domain comparison (DOM-008 AC#2).</item>
///   <item><see cref="AssertionKind.UrlEquals"/> or <see cref="AssertionKind.TextEquals"/>
///         is used without supplying an <see cref="AssertionSpec.ExpectedValue"/>.</item>
/// </list>
///
/// Authority: DOM-008, BRW-US-010 AC#2, BRW-AT-14.
/// </summary>
public sealed class ConditionParser : IConditionParser
{
    // Comparison operator tokens that indicate a numeric / business comparison expression.
    // Checked in order so that multi-character operators are matched before their single-char prefixes.
    private static readonly string[] ComparisonOperators =
    [
        ">=",
        "<=",
        "!=",
        ">",
        "<"
    ];

    /// <inheritdoc/>
    public ConditionParseResult Validate(AssertionSpec spec)
    {
        // AC#2 / BRW-AT-14: A RawExpression is always a business predicate — reject immediately.
        // This allows test fixtures to represent "price > 100" style expressions without embedding
        // them as an ExpectedValue (which would also be rejected, but with a different message).
        if (spec.RawExpression is not null)
        {
            return ConditionParseResult.Rejected(
                $"Assertion contains a raw expression '{spec.RawExpression}'. " +
                "Business-arithmetic predicates and domain comparison expressions are rejected " +
                "before any browser interaction. Only technical predicates are permitted.",
                "browser.businessPredicateRejected");
        }

        // AC#2: For text/URL equality assertions, reject ExpectedValue strings that contain
        // comparison operators — they indicate a numeric or domain comparison (e.g. "price > 100").
        if (spec.Kind is AssertionKind.TextEquals or AssertionKind.UrlEquals &&
            spec.ExpectedValue is { } value)
        {
            foreach (var op in ComparisonOperators)
            {
                if (value.Contains(op, StringComparison.Ordinal))
                {
                    return ConditionParseResult.Rejected(
                        $"AssertionSpec.ExpectedValue '{value}' contains the comparison operator " +
                        $"'{op}'. Numeric and business comparison predicates (e.g. 'price > 100') " +
                        "are rejected before any browser interaction. Use exact technical text equality only.",
                        "browser.businessPredicateRejected");
                }
            }
        }

        // For URL/Text equality, ExpectedValue is required.
        if (spec.Kind is AssertionKind.UrlEquals or AssertionKind.TextEquals &&
            string.IsNullOrEmpty(spec.ExpectedValue))
        {
            return ConditionParseResult.Rejected(
                $"AssertionKind.{spec.Kind} requires a non-empty ExpectedValue to compare against.",
                "browser.selectorRefInvalid");
        }

        return ConditionParseResult.Valid();
    }
}
