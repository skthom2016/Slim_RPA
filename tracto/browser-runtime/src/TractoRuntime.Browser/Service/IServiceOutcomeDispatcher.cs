using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Service;

/// <summary>
/// CEC-005 / CEC-013 / CEC-016 — Maps a <see cref="ServiceInvokeResult"/> to an
/// <see cref="OutcomeDispatchDecision"/> that tells the run engine what to do next.
///
/// <para><b>BRW-US-018 (All Five Service Outcomes):</b></para>
/// <list type="bullet">
///   <item><see cref="ServiceOutcome.SUCCESS"/> — the service call succeeded; follow the
///         configured <c>actionCode</c> UI step if present, then complete as SUCCEEDED.</item>
///   <item><see cref="ServiceOutcome.BUSINESS_REJECTION"/> — the service definitively rejected
///         the request; follow the post-rejection UI step (e.g. display rejection status) if
///         configured; if that UI step also succeeds, complete as SUCCEEDED with
///         <c>businessOutcome=REJECTED</c>.  If the post-rejection UI step itself fails, the
///         run terminates as FAILED for that independent technical reason.  No retry.</item>
///   <item><see cref="ServiceOutcome.TECHNICAL_FAILURE"/> — terminate as FAILED immediately;
///         no <c>actionCode</c> branch is executed.</item>
///   <item><see cref="ServiceOutcome.TRANSIENT_FAILURE"/> — terminate as FAILED immediately;
///         effectful actions are never blindly retried.</item>
///   <item><see cref="ServiceOutcome.UNKNOWN_OUTCOME"/> — terminate as UNKNOWN immediately;
///         <b>no <c>actionCode</c> branch executes</b> until external reconciliation
///         establishes a safe proven outcome (CEC-016 / BRW-US-018 AC#3).</item>
/// </list>
/// </summary>
public interface IServiceOutcomeDispatcher
{
    /// <summary>
    /// Converts <paramref name="result"/> from a <c>service.invoke</c> call into an
    /// <see cref="OutcomeDispatchDecision"/> the run engine can act upon.
    /// </summary>
    OutcomeDispatchDecision Dispatch(ServiceInvokeResult result);
}

/// <summary>
/// Decision produced by <see cref="IServiceOutcomeDispatcher.Dispatch"/> for one
/// <see cref="ServiceInvokeResult"/>.
///
/// <para>The run engine applies it as follows:</para>
/// <list type="number">
///   <item>When <see cref="ShouldExecuteActionCode"/> is <see langword="false"/>: terminate
///         the run immediately with <see cref="TerminalRunState"/>.  Do not execute any
///         <c>actionCode</c> step — especially critical for
///         <see cref="ServiceOutcome.UNKNOWN_OUTCOME"/>.</item>
///   <item>When <see cref="ShouldExecuteActionCode"/> is <see langword="true"/>: execute the
///         <see cref="ActionCode"/>-keyed UI step.
///         <list type="bullet">
///           <item>If that step succeeds: complete the run with <see cref="TerminalRunState"/>
///                 and, when set, record <see cref="BusinessOutcome"/> in the result.</item>
///           <item>If that step fails: complete the run as FAILED regardless of
///                 <see cref="TerminalRunState"/> (the technical UI failure is independent
///                 of the service outcome — BRW-US-018 AC#2).</item>
///         </list>
///   </item>
/// </list>
/// </summary>
public sealed record OutcomeDispatchDecision
{
    /// <summary>The service outcome that produced this decision.</summary>
    public required ServiceOutcome ServiceOutcome { get; init; }

    /// <summary>
    /// The terminal run state to use when the run ends (after any actionCode step, if one runs).
    /// <para>When <see cref="ServiceOutcome"/> is <see cref="Contracts.ServiceOutcome.UNKNOWN_OUTCOME"/>
    /// this is always <see cref="RunState.UNKNOWN"/>.</para>
    /// </summary>
    public required RunState TerminalRunState { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the run engine must execute the UI step identified by
    /// <see cref="ActionCode"/> before determining the final run outcome.
    ///
    /// <para><b>CRITICAL:</b> This is always <see langword="false"/> when
    /// <see cref="ServiceOutcome"/> is <see cref="Contracts.ServiceOutcome.UNKNOWN_OUTCOME"/>.
    /// No actionCode branch may execute before external reconciliation (CEC-016 / BRW-US-018 AC#3).</para>
    /// </summary>
    public bool ShouldExecuteActionCode { get; init; }

    /// <summary>
    /// The <c>actionCode</c> returned by the service, if any.
    /// Only meaningful when <see cref="ShouldExecuteActionCode"/> is <see langword="true"/>.
    /// </summary>
    public string? ActionCode { get; init; }

    /// <summary>
    /// Set to <c>"REJECTED"</c> when <see cref="ServiceOutcome"/> is
    /// <see cref="Contracts.ServiceOutcome.BUSINESS_REJECTION"/>.
    /// The run engine must include this value in <see cref="Contracts.CecRunResult.BusinessOutcome"/>
    /// when the run completes as SUCCEEDED.
    /// <c>null</c> for all other outcomes.
    /// </summary>
    public string? BusinessOutcome { get; init; }
}
