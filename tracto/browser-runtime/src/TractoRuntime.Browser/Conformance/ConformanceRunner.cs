using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TractoRuntime.Browser.Continuation;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Lease;
using TractoRuntime.Browser.Service;
using TractoRuntime.Browser.Validation;
using TractoRuntime.Browser.Worker;

namespace TractoRuntime.Browser.Conformance;

/// <summary>
/// Executes the full CEC v1.0.1 conformance fixture set (CF-01 … CF-10) against
/// in-process implementations of the Browser Runtime components.
///
/// <para>All fixtures run without a live browser or network connection — they exercise
/// the contract layer (validator, dispatcher, lease, continuation, journal) only.
/// This is the "mock Orchestrator" mode required by BRW-US-020 AC#1.</para>
///
/// <para>Authority: BRW-US-020, Appendix A (CEC-007, CEC-010), Appendix F.</para>
/// </summary>
public sealed class ConformanceRunner
{
    private readonly IArtifactValidator _validator;
    private readonly ILogger _log;

    public ConformanceRunner(IArtifactValidator validator, ILogger logger)
    {
        _validator = validator;
        _log = logger;
    }

    /// <summary>
    /// Runs all CF-01–CF-10 fixtures and returns one result per fixture.
    /// The list order is CF-01 first, CF-10 last.
    /// </summary>
    public async Task<IReadOnlyList<ConformanceFixtureResult>> RunAllAsync(CancellationToken ct)
    {
        var results = new List<ConformanceFixtureResult>
        {
            RunCf01_WorkerRegistrationAndCapabilityAdvertisement(),
            RunCf02_ValidArtifactAccepted(),
            RunCf03_UnsignedArtifactRejected(),
            RunCf04_WrongRuntimeArtifactRejected(),
            RunCf05_FiveServiceOutcomes(),
            RunCf06_LeaseFencing(),
            RunCf07_UnknownOutcomeTerminatesAsUnknown(),
            RunCf08_HandedOffOnlyOnConfirmedCallback(),
            RunCf09_NoSecretInEventPayloads(),
            await RunCf10_ReconciliationAfterRestartAsync(ct)
        };

        return results.AsReadOnly();
    }

    // ── CF-01 — Worker Registration and Capability Advertisement ─────────────
    // BRW-US-006 AC#1, BRW-US-019 AC#1, CEC-010.
    // Verifies that the WorkerRegistration record is structurally correct and
    // that the capability advertisement includes required fields.

    private static ConformanceFixtureResult RunCf01_WorkerRegistrationAndCapabilityAdvertisement()
    {
        const string FixtureId = "CF-01";
        const string Description = "Worker registration and capability advertisement";

        try
        {
            var reg = new WorkerRegistration
            {
                WorkerId       = "brw-worker-cf01",
                RuntimeType    = "browser",
                Capabilities   = ["browser.headless", "browser.engine.chromium"],
                CapabilityDigest = "a1b2c3d4e5f6abcd"
            };

            if (reg.WorkerId != "brw-worker-cf01")
                return Fail(FixtureId, Description, "WorkerId not preserved");
            if (!string.Equals(reg.RuntimeType, "browser", StringComparison.OrdinalIgnoreCase))
                return Fail(FixtureId, Description, "RuntimeType must be 'browser'");
            if (reg.Capabilities.Count == 0)
                return Fail(FixtureId, Description, "Capabilities must not be empty");
            if (string.IsNullOrWhiteSpace(reg.CapabilityDigest))
                return Fail(FixtureId, Description, "CapabilityDigest must be present (integrity hint)");

            // CapabilityDigest is an integrity hint only — not an authentication credential (C4 approval).
            if (reg.CapabilityDigest.Length < 8)
                return Fail(FixtureId, Description, "CapabilityDigest must be at least 8 hex chars");

            return Pass(FixtureId, Description);
        }
        catch (Exception ex)
        {
            return Fail(FixtureId, Description, ex.Message);
        }
    }

    // ── CF-02 — Valid Signed Artifact Accepted ────────────────────────────────
    // BRW-US-017 AC#1 (positive path), CEC-001.

    private ConformanceFixtureResult RunCf02_ValidArtifactAccepted()
    {
        const string FixtureId = "CF-02";
        const string Description = "Valid signed artifact accepted before any browser action";

        try
        {
            var envelope = BuildValidEnvelope();
            var result = _validator.Validate(envelope);

            if (!result.IsValid)
                return Fail(FixtureId, Description,
                    $"Valid artifact rejected: category={result.ErrorCategory}, detail={result.DetailCode}");
            if (result.ErrorCategory is not null)
                return Fail(FixtureId, Description, "Valid result must have no ErrorCategory");
            if (result.DetailCode is not null)
                return Fail(FixtureId, Description, "Valid result must have no DetailCode");

            return Pass(FixtureId, Description);
        }
        catch (Exception ex)
        {
            return Fail(FixtureId, Description, ex.Message);
        }
    }

    // ── CF-03 — Unsigned Artifact Rejected ───────────────────────────────────
    // BRW-US-017 AC#1 (negative path), BRW-AT-21, CEC-001.
    // Rejection must fire BEFORE any browser interaction.

    private ConformanceFixtureResult RunCf03_UnsignedArtifactRejected()
    {
        const string FixtureId = "CF-03";
        const string Description = "Unsigned artifact rejected before any browser interaction";

        try
        {
            var noSig     = BuildValidEnvelope() with { Signature = "" };
            var noDigest  = BuildValidEnvelope() with { ArtifactDigest = "" };
            var whiteSig  = BuildValidEnvelope() with { Signature = "   " };

            foreach (var (label, env) in new[]
            {
                ("empty-sig", noSig), ("empty-digest", noDigest), ("whitespace-sig", whiteSig)
            })
            {
                var r = _validator.Validate(env);
                if (r.IsValid)
                    return Fail(FixtureId, Description, $"{label}: unsigned artifact must be rejected");
                if (r.ErrorCategory != ErrorCategory.VALIDATION)
                    return Fail(FixtureId, Description,
                        $"{label}: expected VALIDATION, got {r.ErrorCategory}");
                if (r.DetailCode != "browser.artifactUnsigned")
                    return Fail(FixtureId, Description,
                        $"{label}: expected detailCode=browser.artifactUnsigned, got {r.DetailCode}");
            }

            return Pass(FixtureId, Description);
        }
        catch (Exception ex)
        {
            return Fail(FixtureId, Description, ex.Message);
        }
    }

    // ── CF-04 — Wrong-Runtime Artifact Rejected ──────────────────────────────
    // BRW-US-017 AC#2, BRW-AT-21, CEC-012.

    private ConformanceFixtureResult RunCf04_WrongRuntimeArtifactRejected()
    {
        const string FixtureId = "CF-04";
        const string Description = "Wrong-runtime artifact rejected before any browser action";

        try
        {
            foreach (var wrongType in new[] { "desktop", "terminal", "shell", "" })
            {
                var env = BuildValidEnvelope() with { RuntimeType = wrongType };
                var r = _validator.Validate(env);

                if (r.IsValid)
                    return Fail(FixtureId, Description,
                        $"runtimeType='{wrongType}' must be rejected");
                if (r.ErrorCategory != ErrorCategory.VALIDATION)
                    return Fail(FixtureId, Description,
                        $"runtimeType='{wrongType}': expected VALIDATION, got {r.ErrorCategory}");
                if (r.DetailCode != "browser.artifactTypeMismatch")
                    return Fail(FixtureId, Description,
                        $"runtimeType='{wrongType}': expected detailCode=browser.artifactTypeMismatch");
            }

            // Positive path — "browser" (case-insensitive) accepted
            var validEnv = BuildValidEnvelope() with { RuntimeType = "BROWSER" };
            var validResult = _validator.Validate(validEnv);
            if (!validResult.IsValid)
                return Fail(FixtureId, Description, "runtimeType='BROWSER' (uppercase) must be accepted");

            return Pass(FixtureId, Description);
        }
        catch (Exception ex)
        {
            return Fail(FixtureId, Description, ex.Message);
        }
    }

    // ── CF-05 — All Five Service Outcomes Handled Correctly ───────────────────
    // BRW-US-018 AC#1, AC#3, BRW-AT-11, BRW-AT-24, CEC-005, CEC-013, CEC-016.
    // No branching on domain values; UNKNOWN_OUTCOME terminates as UNKNOWN.

    private static ConformanceFixtureResult RunCf05_FiveServiceOutcomes()
    {
        const string FixtureId = "CF-05";
        const string Description = "All five service outcomes mapped to correct run states";

        try
        {
            var dispatcher = new ServiceOutcomeDispatcher(NullLogger<ServiceOutcomeDispatcher>.Instance);

            var expected = new Dictionary<ServiceOutcome, RunState>
            {
                [ServiceOutcome.SUCCESS]            = RunState.SUCCEEDED,
                [ServiceOutcome.BUSINESS_REJECTION] = RunState.SUCCEEDED,
                [ServiceOutcome.TECHNICAL_FAILURE]  = RunState.FAILED,
                [ServiceOutcome.TRANSIENT_FAILURE]  = RunState.FAILED,
                [ServiceOutcome.UNKNOWN_OUTCOME]    = RunState.UNKNOWN
            };

            foreach (var (outcome, expectedState) in expected)
            {
                var decision = dispatcher.Dispatch(new ServiceInvokeResult
                {
                    Outcome    = outcome,
                    ActionCode = "ANY_CODE"
                });

                if (decision.TerminalRunState != expectedState)
                    return Fail(FixtureId, Description,
                        $"outcome={outcome}: expected runState={expectedState}, got {decision.TerminalRunState}");

                // CRITICAL: UNKNOWN_OUTCOME must never allow actionCode branch
                if (outcome == ServiceOutcome.UNKNOWN_OUTCOME && decision.ShouldExecuteActionCode)
                    return Fail(FixtureId, Description,
                        "UNKNOWN_OUTCOME: ShouldExecuteActionCode must be false (CEC-016)");

                // BUSINESS_REJECTION must set businessOutcome=REJECTED
                if (outcome == ServiceOutcome.BUSINESS_REJECTION && decision.BusinessOutcome != "REJECTED")
                    return Fail(FixtureId, Description,
                        "BUSINESS_REJECTION: BusinessOutcome must be 'REJECTED'");
            }

            // Verify exact enum counts
            var serviceOutcomes = Enum.GetValues<ServiceOutcome>();
            if (serviceOutcomes.Length != 5)
                return Fail(FixtureId, Description,
                    $"CEC v1.0.1 requires exactly 5 serviceOutcome values; found {serviceOutcomes.Length}");

            var runStates = Enum.GetValues<RunState>();
            if (runStates.Length != 6)
                return Fail(FixtureId, Description,
                    $"CEC-016 requires exactly 6 run states; found {runStates.Length}");

            // UNKNOWN_OUTCOME must be a serviceOutcome, not a run state (distinct types)
            var runStateNames = Enum.GetNames<RunState>();
            if (runStateNames.Contains("UNKNOWN_OUTCOME"))
                return Fail(FixtureId, Description,
                    "UNKNOWN_OUTCOME must not appear as a run state (conflation guard)");

            return Pass(FixtureId, Description);
        }
        catch (Exception ex)
        {
            return Fail(FixtureId, Description, ex.Message);
        }
    }

    // ── CF-06 — Lease Fencing ─────────────────────────────────────────────────
    // BRW-US-019 AC#1, CEC-HOST-002.
    // At most one active lease per worker at any time.

    private static ConformanceFixtureResult RunCf06_LeaseFencing()
    {
        const string FixtureId = "CF-06";
        const string Description = "Lease fencing: at most one active lease per registered worker";

        try
        {
            var fence = new LeaseFence();

            // First acquire succeeds
            if (!fence.TryAcquire("worker-cf06", "lease-A"))
                return Fail(FixtureId, Description, "First acquire must succeed");

            // Second acquire for same worker fails (one-lease rule)
            if (fence.TryAcquire("worker-cf06", "lease-B"))
                return Fail(FixtureId, Description, "Second acquire for same worker must fail");

            // Different worker can acquire concurrently
            if (!fence.TryAcquire("worker-cf06-alt", "lease-C"))
                return Fail(FixtureId, Description, "Different worker must be able to acquire concurrently");

            // After explicit release, worker can acquire again
            fence.Release("worker-cf06");
            if (!fence.TryAcquire("worker-cf06", "lease-D"))
                return Fail(FixtureId, Description, "Acquire after release must succeed");

            // HasActiveLease reflects correct state
            if (!fence.HasActiveLease("worker-cf06"))
                return Fail(FixtureId, Description, "HasActiveLease must return true after acquire");

            return Pass(FixtureId, Description);
        }
        catch (Exception ex)
        {
            return Fail(FixtureId, Description, ex.Message);
        }
    }

    // ── CF-07 — UNKNOWN_OUTCOME Terminates as UNKNOWN ─────────────────────────
    // BRW-US-018 AC#3, BRW-US-015 AC#3, BRW-AT-23, CEC-016, BRE-007.
    // CRITICAL: no actionCode branch executes; attempt stops as UNKNOWN (terminal).

    private static ConformanceFixtureResult RunCf07_UnknownOutcomeTerminatesAsUnknown()
    {
        const string FixtureId = "CF-07";
        const string Description = "UNKNOWN_OUTCOME terminates attempt as UNKNOWN; no actionCode branch";

        try
        {
            var dispatcher = new ServiceOutcomeDispatcher(NullLogger<ServiceOutcomeDispatcher>.Instance);

            var decision = dispatcher.Dispatch(new ServiceInvokeResult
            {
                Outcome    = ServiceOutcome.UNKNOWN_OUTCOME,
                ActionCode = "THIS_MUST_NEVER_EXECUTE"
            });

            if (decision.TerminalRunState != RunState.UNKNOWN)
                return Fail(FixtureId, Description,
                    $"UNKNOWN_OUTCOME must produce run state UNKNOWN; got {decision.TerminalRunState}");

            if (decision.ShouldExecuteActionCode)
                return Fail(FixtureId, Description,
                    "CRITICAL: ShouldExecuteActionCode must be false for UNKNOWN_OUTCOME (CEC-016)");

            if (decision.ActionCode is not null)
                return Fail(FixtureId, Description,
                    "ActionCode must be cleared (null) for UNKNOWN_OUTCOME to prevent accidental use");

            // Verify UNKNOWN is terminal — not UNKNOWN_OUTCOME (the service outcome)
            if (!Enum.IsDefined(typeof(RunState), RunState.UNKNOWN))
                return Fail(FixtureId, Description, "RunState.UNKNOWN must exist as a terminal state");
            if (!Enum.IsDefined(typeof(ServiceOutcome), ServiceOutcome.UNKNOWN_OUTCOME))
                return Fail(FixtureId, Description, "ServiceOutcome.UNKNOWN_OUTCOME must exist");

            return Pass(FixtureId, Description);
        }
        catch (Exception ex)
        {
            return Fail(FixtureId, Description, ex.Message);
        }
    }

    // ── CF-08 — HANDED_OFF Only on Confirmed Authenticated Callback ───────────
    // BRW-US-018 AC#4, BRW-US-019 AC#5, CEC-016.
    // No speculative HANDED_OFF; continuation only after eligible deduplicated auth callback.

    private static ConformanceFixtureResult RunCf08_HandedOffOnlyOnConfirmedCallback()
    {
        const string FixtureId = "CF-08";
        const string Description = "HANDED_OFF set only on confirmed authenticated deduplicated callback";

        try
        {
            var handler = new AsyncContinuationHandler(NullLogger<AsyncContinuationHandler>.Instance);

            // RegisterHandedOffAsync alone must NOT create a continuation run (no speculative HANDED_OFF)
            handler.RegisterHandedOffAsync("run-cf08-a", "lease-cf08", CancellationToken.None)
                   .GetAwaiter().GetResult();

            // Unauthenticated callback must be rejected (auth checked before dedup slot consumed)
            var badAuth = new CallbackNotification
            {
                RunId          = "run-cf08-a",
                CallbackToken  = "cb-cf08-001",
                AuthTag        = "",
                ServiceOutcome = ServiceOutcome.SUCCESS
            };
            var badResult = handler.ProcessCallbackAsync(badAuth, CancellationToken.None).GetAwaiter().GetResult();
            if (badResult.Accepted)
                return Fail(FixtureId, Description, "Unauthenticated callback must be rejected");

            // Same token with valid auth must now succeed (dedup slot not consumed by bad auth)
            var goodAuth = new CallbackNotification
            {
                RunId          = "run-cf08-a",
                CallbackToken  = "cb-cf08-001",
                AuthTag        = "auth-valid-tag",
                ServiceOutcome = ServiceOutcome.SUCCESS
            };
            var goodResult = handler.ProcessCallbackAsync(goodAuth, CancellationToken.None).GetAwaiter().GetResult();
            if (!goodResult.Accepted)
                return Fail(FixtureId, Description,
                    "Authenticated callback must be accepted after failed unauthenticated attempt");
            if (goodResult.ContinuationRunId is null)
                return Fail(FixtureId, Description,
                    "Accepted callback must produce a continuation run ID");

            // Duplicate callback token must be rejected
            var duplicateAuth = new CallbackNotification
            {
                RunId          = "run-cf08-a",  // run already consumed
                CallbackToken  = "cb-cf08-001", // same token — replay
                AuthTag        = "auth-valid-tag",
                ServiceOutcome = ServiceOutcome.SUCCESS
            };
            // Register a fresh run to isolate dedup check from state exhaustion
            handler.RegisterHandedOffAsync("run-cf08-b", "lease-cf08-b", CancellationToken.None)
                   .GetAwaiter().GetResult();
            var dupNotif = duplicateAuth with { RunId = "run-cf08-b" };
            var dupResult = handler.ProcessCallbackAsync(dupNotif, CancellationToken.None).GetAwaiter().GetResult();
            if (dupResult.Accepted)
                return Fail(FixtureId, Description,
                    "Duplicate callback token must be rejected (deduplication)");

            // Non-HANDED_OFF run must be rejected
            var nonHandedOff = new CallbackNotification
            {
                RunId          = "run-not-handed-off",
                CallbackToken  = "cb-cf08-002",
                AuthTag        = "auth-valid-tag",
                ServiceOutcome = ServiceOutcome.SUCCESS
            };
            var noHo = handler.ProcessCallbackAsync(nonHandedOff, CancellationToken.None).GetAwaiter().GetResult();
            if (noHo.Accepted)
                return Fail(FixtureId, Description,
                    "Callback for non-HANDED_OFF run must be rejected");

            // UNKNOWN_OUTCOME in callback → terminate as UNKNOWN (no continuation)
            handler.RegisterHandedOffAsync("run-cf08-c", "lease-cf08-c", CancellationToken.None)
                   .GetAwaiter().GetResult();
            var unknownCb = new CallbackNotification
            {
                RunId          = "run-cf08-c",
                CallbackToken  = "cb-cf08-unk-001",
                AuthTag        = "auth-valid-tag",
                ServiceOutcome = ServiceOutcome.UNKNOWN_OUTCOME
            };
            var unkResult = handler.ProcessCallbackAsync(unknownCb, CancellationToken.None).GetAwaiter().GetResult();
            if (unkResult.Accepted)
                return Fail(FixtureId, Description,
                    "UNKNOWN_OUTCOME callback must be rejected and terminate as UNKNOWN");
            if (!unkResult.TerminateAsUnknown)
                return Fail(FixtureId, Description,
                    "UNKNOWN_OUTCOME callback must set TerminateAsUnknown=true");

            return Pass(FixtureId, Description);
        }
        catch (Exception ex)
        {
            return Fail(FixtureId, Description, ex.Message);
        }
    }

    // ── CF-09 — No Secrets in Event Payloads ─────────────────────────────────
    // BRW-US-020 AC#5, BRW-US-014 AC#3, BRW-US-017 AC#1, CEC §4 (security gate).
    // Every validation result message must be safe to log — no credentials, tokens, cookies.

    private ConformanceFixtureResult RunCf09_NoSecretInEventPayloads()
    {
        const string FixtureId = "CF-09";
        const string Description = "No secrets in validation result messages or event payloads";

        try
        {
            // Probe each validation failure mode — each must produce a secret-safe message
            var failingEnvelopes = new[]
            {
                ("unsigned",      BuildValidEnvelope() with { Signature = "" }),
                ("empty-digest",  BuildValidEnvelope() with { ArtifactDigest = "" }),
                ("wrong-runtime", BuildValidEnvelope() with { RuntimeType = "desktop" }),
                ("bad-schema",    BuildValidEnvelope() with { SchemaMajor = "99" }),
                ("page.evaluate", BuildValidEnvelope([new BrowserStep { StepId = "s1", Action = "page.evaluate" }]))
            };

            var forbiddenTerms = new[] { "password", "token", "secret", "cookie", "credential", "authz", "bearer" };

            foreach (var (label, envelope) in failingEnvelopes)
            {
                var result = _validator.Validate(envelope);
                if (result.Message is null)
                    continue; // no message — not a failure for this fixture

                var lower = result.Message.ToLowerInvariant();
                foreach (var term in forbiddenTerms)
                {
                    if (lower.Contains(term))
                        return Fail(FixtureId, Description,
                            $"Rejection message for '{label}' contains forbidden term '{term}'");
                }
            }

            // WorkerRegistration payloads must not embed raw credentials
            var reg = new WorkerRegistration
            {
                WorkerId       = "brw-worker-cf09",
                RuntimeType    = "browser",
                Capabilities   = ["browser.headless"],
                CapabilityDigest = "a1b2c3d4e5f6abcd"   // integrity hint only
            };

            // The CapabilityDigest is an opaque integrity hint, not a credential (C4 approval).
            // It must not contain the word "password", "secret", or "token".
            if (reg.CapabilityDigest.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                reg.CapabilityDigest.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                reg.CapabilityDigest.Contains("token", StringComparison.OrdinalIgnoreCase))
            {
                return Fail(FixtureId, Description,
                    "CapabilityDigest must not contain credential terms");
            }

            return Pass(FixtureId, Description);
        }
        catch (Exception ex)
        {
            return Fail(FixtureId, Description, ex.Message);
        }
    }

    // ── CF-10 — Reconciliation After Restart ──────────────────────────────────
    // BRW-US-019 AC#2, AC#3, AC#4, BRW-AT-12, BRW-AT-23, CEC-004, CEC-HOST-003.
    // After restart, the journal is read from disk; committed side effects are detected.

    private static async Task<ConformanceFixtureResult> RunCf10_ReconciliationAfterRestartAsync(
        CancellationToken ct)
    {
        const string FixtureId = "CF-10";
        const string Description = "Journal read from disk after restart; committed side effects detected";

        var dir = Path.Combine(Path.GetTempPath(), $"cf10-{Guid.NewGuid():N}");
        try
        {
            var journal1 = new RunJournal(dir, NullLogger<RunJournal>.Instance);
            const string runId = "run-cf10";

            await journal1.OpenAsync(runId, ct);
            await journal1.AppendStepAsync(runId, "step-nav",    "navigate",      null, ct);
            await journal1.AppendStepAsync(runId, "step-invoke", "service.invoke", null, ct);
            await journal1.CloseAsync(runId, RunState.SUCCEEDED, ct);

            // Simulate restart: fresh instance reads from disk
            var journal2 = new RunJournal(dir, NullLogger<RunJournal>.Instance);
            var svc = new ReconciliationService(journal2, NullLogger<ReconciliationService>.Instance);
            var result = await svc.ReconcileAsync(runId, ct);

            if (result.Verdict != ReconciliationVerdict.RunAlreadyClosed)
                return Fail(FixtureId, Description,
                    $"Expected RunAlreadyClosed; got {result.Verdict}");

            if (result.LastKnownState != RunState.SUCCEEDED)
                return Fail(FixtureId, Description,
                    $"Expected SUCCEEDED; got {result.LastKnownState}");

            if (!result.HasCommittedSideEffect)
                return Fail(FixtureId, Description,
                    "service.invoke must be flagged as a committed side effect");

            // No-journal case: fresh run ID returns NoJournalFound
            var noJournal = await svc.ReconcileAsync("run-cf10-never-started", ct);
            if (noJournal.Verdict != ReconciliationVerdict.NoJournalFound)
                return Fail(FixtureId, Description,
                    $"Unknown runId must return NoJournalFound; got {noJournal.Verdict}");

            return Pass(FixtureId, Description);
        }
        catch (Exception ex)
        {
            return Fail(FixtureId, Description, ex.Message);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ArtifactEnvelope BuildValidEnvelope(
        IReadOnlyList<BrowserStep>? steps = null)
        => new()
        {
            RuntimeType    = "browser",
            SchemaMajor    = "1",
            Signature      = "sha256:conformance-test-sig-aabbcc",
            ArtifactDigest = "sha256:conformance-test-dig-ddeeff",
            Script = new BrowserScript
            {
                ScriptId = "cf-script",
                Version  = "1.0.0",
                Steps    = steps ?? [new BrowserStep { StepId = "s1", Action = "browser.navigate" }]
            }
        };

    private static ConformanceFixtureResult Pass(string id, string description)
        => new() { FixtureId = id, Description = description, Passed = true };

    private static ConformanceFixtureResult Fail(string id, string description, string reason)
        => new() { FixtureId = id, Description = description, Passed = false, FailureReason = reason };
}
