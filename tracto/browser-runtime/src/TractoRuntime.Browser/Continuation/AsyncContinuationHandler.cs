using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Continuation;

/// <summary>
/// CEC-016 implementation — async continuation gate for HANDED_OFF runs.
///
/// <para><b>HANDED_OFF semantics (CEC §5.4 / BRW-US-018 AC4, BRW-US-019 AC5):</b></para>
/// <list type="bullet">
///   <item>HANDED_OFF is set ONLY on a confirmed async acknowledgement — never speculatively.</item>
///   <item>A continuation run is created by the Orchestrator ONLY when an eligible,
///         authenticated, and deduplicated callback arrives after HANDED_OFF is persisted.</item>
///   <item><see cref="ServiceOutcome.UNKNOWN_OUTCOME"/> in the callback terminates the
///         attempt as UNKNOWN; no continuation run is created and no actionCode branch executes.</item>
///   <item>The service cannot supply a scriptRef; the continuation script is statically
///         configured in the Orchestrator.</item>
/// </list>
/// </summary>
public sealed class AsyncContinuationHandler(ILogger<AsyncContinuationHandler> logger)
    : IAsyncContinuationHandler
{
    // runId → leaseToken for runs persisted as HANDED_OFF
    private readonly ConcurrentDictionary<string, string> _handedOffRuns = new();

    // Deduplicated callback tokens — prevents replaying the same callback twice
    private readonly ConcurrentDictionary<string, bool> _seenCallbackTokens = new();

    /// <inheritdoc/>
    public Task RegisterHandedOffAsync(string runId, string leaseToken, CancellationToken ct)
    {
        // Record only after the HANDED_OFF state is durably persisted — never before
        _handedOffRuns[runId] = leaseToken;
        logger.LogInformation(
            "Run {RunId} registered as HANDED_OFF (leaseToken={LeaseToken}). " +
            "Awaiting eligible authenticated callback. No continuation run created at this time.",
            runId, leaseToken);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<ContinuationResult> ProcessCallbackAsync(
        CallbackNotification notification,
        CancellationToken ct)
    {
        // ── Gate 1: Source run must be in HANDED_OFF state ───────────────────
        if (!_handedOffRuns.ContainsKey(notification.RunId))
        {
            logger.LogWarning(
                "Callback {CallbackToken} for run {RunId} rejected: " +
                "run is not in HANDED_OFF state.",
                notification.CallbackToken, notification.RunId);
            return Task.FromResult(new ContinuationResult
            {
                Accepted = false,
                Reason   = "Run is not in HANDED_OFF state"
            });
        }

        // ── Gate 2: Authentication — AuthTag must be present and non-empty ─────
        // Auth is checked BEFORE consuming the deduplication slot so that an
        // unauthenticated call does not permanently burn the callback token.
        // A legitimate retry (same token, correct auth) must still be accepted.
        if (string.IsNullOrWhiteSpace(notification.AuthTag))
        {
            logger.LogWarning(
                "Callback {CallbackToken} for run {RunId} rejected: missing authentication tag.",
                notification.CallbackToken, notification.RunId);
            return Task.FromResult(new ContinuationResult
            {
                Accepted = false,
                Reason   = "Missing authentication tag"
            });
        }

        // ── Gate 3: Deduplicate — never process the same callback token twice ─
        // Only consumes the dedup slot after authentication succeeds, so that
        // a rejected (unauthenticated) call cannot poison the dedup set.
        if (!_seenCallbackTokens.TryAdd(notification.CallbackToken, true))
        {
            logger.LogWarning(
                "Callback {CallbackToken} for run {RunId} rejected: duplicate callback token.",
                notification.CallbackToken, notification.RunId);
            return Task.FromResult(new ContinuationResult
            {
                Accepted = false,
                Reason   = "Duplicate callback token"
            });
        }

        // ── Gate 4: UNKNOWN_OUTCOME → terminate as UNKNOWN, no continuation ───
        // CEC: "UNKNOWN_OUTCOME terminates the attempt as UNKNOWN; no actionCode branch
        //       executes before external reconciliation."
        if (notification.ServiceOutcome == ServiceOutcome.UNKNOWN_OUTCOME)
        {
            _handedOffRuns.TryRemove(notification.RunId, out _);
            logger.LogWarning(
                "Callback {CallbackToken} for run {RunId}: ServiceOutcome=UNKNOWN_OUTCOME. " +
                "Attempt terminates as UNKNOWN. No continuation run created.",
                notification.CallbackToken, notification.RunId);
            return Task.FromResult(new ContinuationResult
            {
                Accepted           = false,
                Reason             = "UNKNOWN_OUTCOME prevents continuation; attempt terminates as UNKNOWN",
                TerminateAsUnknown = true
            });
        }

        // ── All gates passed: eligible callback → Orchestrator creates continuation ─
        // The service cannot supply a scriptRef; the continuation is statically configured.
        _handedOffRuns.TryRemove(notification.RunId, out _);
        var continuationRunId = $"{notification.RunId}-cont-{Guid.NewGuid():N}";

        logger.LogInformation(
            "Callback {CallbackToken} for run {RunId} accepted (outcome={Outcome}). " +
            "Continuation run {ContinuationRunId} will be created by the Orchestrator.",
            notification.CallbackToken, notification.RunId,
            notification.ServiceOutcome, continuationRunId);

        return Task.FromResult(new ContinuationResult
        {
            Accepted          = true,
            ContinuationRunId = continuationRunId,
            ServiceOutcome    = notification.ServiceOutcome
        });
    }
}

// ContinuationResult is defined in ContinuationResult.cs in this namespace.
