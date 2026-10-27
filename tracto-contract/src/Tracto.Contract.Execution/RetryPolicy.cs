#nullable enable

using Tracto.Contract.Models;

namespace Tracto.Contract.Execution;

// Determines whether a step may be retried after a failure outcome.
// C3 APPROVED rules:
// - FAILED + TRANSIENT_SAFE → bounded retry permitted if step has not committed a side effect
// - UNKNOWN → NEVER retry blindly; reconcile or operator investigation required
// - TIMED_OUT + TRANSIENT_SAFE + confirmed non-commitment → bounded retry permitted
// - All other cases → no retry
public sealed class RetryPolicy
{
    // Returns true if the step may be retried.
    // hasCommittedSideEffect: set by the runtime when it can confirm a side effect occurred.
    // attemptNumber: 1-based count of attempts already made.
    // maxAttempts: maximum attempts before giving up (enforced by caller).
    public bool CanRetry(
        StepState outcomeState,
        RetryClass retryClass,
        bool hasCommittedSideEffect,
        int attemptNumber,
        int maxAttempts)
    {
        // UNKNOWN: NEVER retry (C3 rule, fencing)
        if (outcomeState == StepState.Unknown) return false;

        // FAILED: retry only if TRANSIENT_SAFE and no side effect committed
        if (outcomeState == StepState.Failed)
        {
            if (retryClass == RetryClass.TransientSafe && !hasCommittedSideEffect && attemptNumber < maxAttempts)
                return true;
            if (retryClass == RetryClass.Idempotent && attemptNumber < maxAttempts)
                return true;
            return false;
        }

        // TIMED_OUT: retry only if non-commitment is confirmed (TRANSIENT_SAFE) and no side effect committed
        if (outcomeState == StepState.TimedOut)
        {
            if (retryClass == RetryClass.TransientSafe && !hasCommittedSideEffect && attemptNumber < maxAttempts)
                return true;
            return false;
        }

        // SUCCEEDED, SKIPPED, CANCELLED, PENDING: no retry needed
        return false;
    }

    // Returns the maximum number of total attempts for a given retryClass.
    // TRANSIENT_SAFE=3, IDEMPOTENT=2, ONCE_ONLY=1 (no retries), NEVER=1 (no retries)
    public static int GetMaxAttempts(RetryClass retryClass) => retryClass switch
    {
        RetryClass.TransientSafe => 3,
        RetryClass.Idempotent    => 2,
        RetryClass.OnceOnly      => 1,
        RetryClass.Never         => 1,
        _                        => 1,
    };
}
