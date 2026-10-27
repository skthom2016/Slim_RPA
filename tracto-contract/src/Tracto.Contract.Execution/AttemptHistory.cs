#nullable enable

using System.Collections.Generic;
using System.Linq;
using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

public sealed class AttemptHistory
{
    private readonly List<RetryAttemptRecord> _attempts = new();

    public IReadOnlyList<RetryAttemptRecord> Attempts => _attempts;
    public int AttemptCount => _attempts.Count;

    public void Record(RetryAttemptRecord attempt) => _attempts.Add(attempt);

    // Returns the final attempt's state, or null if no attempts recorded.
    public StepState? FinalState => _attempts.Count > 0 ? _attempts[^1].OutcomeState : null;

    // Returns true if the attempt history includes any UNKNOWN outcome (fencing check).
    public bool HasUnknownOutcome => _attempts.Any(a => a.OutcomeState == StepState.Unknown);
}
