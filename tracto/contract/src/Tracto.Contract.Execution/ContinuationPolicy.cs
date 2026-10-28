#nullable enable

// Evaluates continuation rules for a run that has reached a terminal state.
// REF-04: No continuation is triggered from HANDED_OFF, FAILED, UNKNOWN, or CANCELLED
// without a configured static continuation rule.

using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

// Evaluates continuation rules for a run that has reached a terminal state.
// REF-04: No continuation is triggered from HANDED_OFF, FAILED, UNKNOWN, or CANCELLED
// without a configured static continuation rule.
public sealed class ContinuationPolicy
{
    private readonly IReadOnlyList<ContinuationRule> _rules;

    public ContinuationPolicy(IReadOnlyList<ContinuationRule> rules) => _rules = rules;

    public static ContinuationPolicy Empty => new(Array.Empty<ContinuationRule>());

    // Returns the first matching continuation rule for the given state, or null if none.
    public ContinuationRule? FindRule(RunState runState)
        => _rules.FirstOrDefault(r => r.TriggerState == runState);

    // Returns true if any continuation is configured for the given state.
    public bool HasContinuation(RunState runState) => FindRule(runState) is not null;
}
