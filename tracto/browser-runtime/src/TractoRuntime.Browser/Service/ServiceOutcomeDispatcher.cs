using Microsoft.Extensions.Logging;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Service;

/// <summary>
/// CEC-005 / CEC-013 / CEC-016 implementation of <see cref="IServiceOutcomeDispatcher"/>.
///
/// <para><b>BRW-US-018 (All Five Service Outcomes and Run State Handling):</b></para>
/// <para>Maps each of the five <see cref="ServiceOutcome"/> values to the correct
/// <see cref="OutcomeDispatchDecision"/> without branching on domain field values.
/// The only decision made here is a technical one: which run state to use and whether
/// the <c>actionCode</c> UI step is eligible to execute.</para>
///
/// <para><b>UNKNOWN_OUTCOME contract (AC#3 / CEC-016 / BRE-007):</b><br/>
/// When the service returns <see cref="ServiceOutcome.UNKNOWN_OUTCOME"/> the business
/// operation's commit status is uncertain.  The runtime <em>must</em> terminate the
/// current attempt as <see cref="RunState.UNKNOWN"/> (terminal; stops the attempt).
/// <see cref="OutcomeDispatchDecision.ShouldExecuteActionCode"/> is set to
/// <see langword="false"/> — no <c>actionCode</c> branch may execute until an authorized
/// external reconciliation step establishes a safe, proven outcome.</para>
///
/// <para><b>BUSINESS_REJECTION contract (AC#2):</b><br/>
/// The service definitively rejected the request.  <see cref="OutcomeDispatchDecision.ShouldExecuteActionCode"/>
/// is set to <see langword="true"/> so the run engine can execute the post-rejection UI step
/// (e.g., display the rejection status to the end user).  If that UI step succeeds, the run
/// completes as <see cref="RunState.SUCCEEDED"/> with
/// <see cref="OutcomeDispatchDecision.BusinessOutcome"/> = <c>"REJECTED"</c>.
/// If the UI step fails, the caller must terminate the run as FAILED — that is an independent
/// technical failure, not a retry.  No retry is attempted for any effectful action.</para>
///
/// <para><b>HANDED_OFF contract (AC#4):</b><br/>
/// <see cref="RunState.HANDED_OFF"/> is set by the run engine <em>after</em> a confirmed
/// async acknowledgement — it is not a <c>ServiceOutcome</c> and is not produced by this
/// dispatcher.  The <see cref="AsyncContinuationHandler"/> manages HANDED_OFF semantics
/// separately.</para>
/// </summary>
public sealed class ServiceOutcomeDispatcher(ILogger<ServiceOutcomeDispatcher> logger)
    : IServiceOutcomeDispatcher
{
    /// <inheritdoc/>
    public OutcomeDispatchDecision Dispatch(ServiceInvokeResult result)
    {
        var decision = result.Outcome switch
        {
            // ── SUCCESS ──────────────────────────────────────────────────────────
            // The service call succeeded.  Follow the configured actionCode UI step
            // if one was returned; otherwise the run completes as SUCCEEDED.
            // No domain field branching: the actionCode is used only as a key to look
            // up the pre-configured next UI step — never to make a business decision.
            ServiceOutcome.SUCCESS => new OutcomeDispatchDecision
            {
                ServiceOutcome        = ServiceOutcome.SUCCESS,
                TerminalRunState      = RunState.SUCCEEDED,
                ShouldExecuteActionCode = result.ActionCode is not null,
                ActionCode            = result.ActionCode,
                BusinessOutcome       = null
            },

            // ── BUSINESS_REJECTION ────────────────────────────────────────────────
            // The service definitively rejected the business request.
            // The run engine should execute the post-rejection UI step (e.g. display
            // rejection status) using the returned actionCode, if present.
            // On success of that UI step → SUCCEEDED with businessOutcome=REJECTED.
            // On failure of that UI step → FAILED (independent technical failure; no retry).
            ServiceOutcome.BUSINESS_REJECTION => new OutcomeDispatchDecision
            {
                ServiceOutcome        = ServiceOutcome.BUSINESS_REJECTION,
                TerminalRunState      = RunState.SUCCEEDED,
                ShouldExecuteActionCode = result.ActionCode is not null,
                ActionCode            = result.ActionCode,
                BusinessOutcome       = "REJECTED"
            },

            // ── TECHNICAL_FAILURE ─────────────────────────────────────────────────
            // A definitive technical failure from the service.  Terminate as FAILED.
            // No actionCode branch; no retry (effectful actions are never blindly replayed).
            ServiceOutcome.TECHNICAL_FAILURE => new OutcomeDispatchDecision
            {
                ServiceOutcome        = ServiceOutcome.TECHNICAL_FAILURE,
                TerminalRunState      = RunState.FAILED,
                ShouldExecuteActionCode = false,
                ActionCode            = null,
                BusinessOutcome       = null
            },

            // ── TRANSIENT_FAILURE ─────────────────────────────────────────────────
            // A transient failure from the service.  Terminate as FAILED.
            // Effectful actions (service.invoke included) are never blindly retried
            // because the business effect may have been committed before the failure.
            ServiceOutcome.TRANSIENT_FAILURE => new OutcomeDispatchDecision
            {
                ServiceOutcome        = ServiceOutcome.TRANSIENT_FAILURE,
                TerminalRunState      = RunState.FAILED,
                ShouldExecuteActionCode = false,
                ActionCode            = null,
                BusinessOutcome       = null
            },

            // ── UNKNOWN_OUTCOME ───────────────────────────────────────────────────
            // CRITICAL (CEC-016 / BRW-US-018 AC#3 / BRE-007):
            // The commitment status of the business operation is uncertain.
            // The runtime MUST terminate the current attempt as UNKNOWN (terminal).
            // ShouldExecuteActionCode = false — no actionCode branch may execute
            // before an authorized external reconciliation step establishes a safe,
            // proven outcome.  UNKNOWN_OUTCOME is a serviceOutcome value, distinct
            // from the terminal run state UNKNOWN.
            ServiceOutcome.UNKNOWN_OUTCOME => new OutcomeDispatchDecision
            {
                ServiceOutcome        = ServiceOutcome.UNKNOWN_OUTCOME,
                TerminalRunState      = RunState.UNKNOWN,
                ShouldExecuteActionCode = false,   // ← must remain false; never execute actionCode
                ActionCode            = null,
                BusinessOutcome       = null
            },

            // ── Guard ─────────────────────────────────────────────────────────────
            // The five values above are exhaustive per CEC v1.0.1.  A sixth value
            // would indicate a contract violation in the service layer.
            _ => throw new InvalidOperationException(
                     $"Unexpected ServiceOutcome value '{result.Outcome}'. " +
                     "CEC v1.0.1 defines exactly five service outcome values.")
        };

        logger.LogInformation(
            "ServiceOutcomeDispatcher: outcome={Outcome} → runState={State}, " +
            "executeActionCode={Execute}, actionCode={Code}, businessOutcome={Business}",
            decision.ServiceOutcome,
            decision.TerminalRunState,
            decision.ShouldExecuteActionCode,
            decision.ActionCode ?? "(none)",
            decision.BusinessOutcome ?? "(none)");

        return decision;
    }
}
