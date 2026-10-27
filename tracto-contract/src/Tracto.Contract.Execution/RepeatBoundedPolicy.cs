#nullable enable

namespace Tracto.Contract.Execution;

// Validates and enforces the maxIterations cap for control.repeatBounded.
// Q9 APPROVED (2026-10-23): DefaultMaxIterations = 100 is normative.
public sealed class RepeatBoundedPolicy
{
    public const int DefaultMaxIterations = 100;

    private readonly int _policyMaxIterations;

    public RepeatBoundedPolicy(int? workerAdvertisedMax = null)
    {
        _policyMaxIterations = workerAdvertisedMax ?? DefaultMaxIterations;
    }

    // Returns a validation error if the requested iterations exceed the policy cap; null if valid.
    public string? Validate(int requestedIterations)
    {
        if (requestedIterations <= 0)
            return $"repeatBounded requires at least 1 iteration; got {requestedIterations}.";
        if (requestedIterations > _policyMaxIterations)
            return $"repeatBounded requested {requestedIterations} iterations; policy cap is {_policyMaxIterations}.";
        return null;
    }

    public bool IsWithinBound(int currentIteration) => currentIteration <= _policyMaxIterations;
}
