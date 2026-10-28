using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Actions;

public interface IActionExecutor
{
    Task<ActionResult> ExecuteAsync(IPage page, BrowserStep step, CancellationToken ct);
}

public sealed record ActionResult
{
    public required bool Succeeded { get; init; }
    public string? ErrorMessage { get; init; }
    public ErrorCategory? ErrorCategory { get; init; }
    public string? DetailCode { get; init; }
    public Dictionary<string, object?> Evidence { get; init; } = new();

    /// <summary>
    /// When <see langword="true"/>, a human operator must review the run before any continuation
    /// is created.  Corresponds to the <c>needsAttention</c> flag in the CEC run result
    /// (BRW-US-012 WEB-009 / AC#5, AC#6).
    /// </summary>
    public bool NeedsAttention { get; init; }

    /// <summary>
    /// Set by <c>service.invoke</c> steps to carry the CEC service outcome back to the
    /// run orchestrator (BRW-US-015 AC#3 / AC#4, CEC-005 / CEC-016).
    ///
    /// <para><b>UNKNOWN_OUTCOME contract (AC#3):</b> when this is
    /// <see cref="Contracts.ServiceOutcome.UNKNOWN_OUTCOME"/> the run orchestrator MUST
    /// terminate the current attempt as UNKNOWN.  No <c>actionCode</c> branch may be
    /// followed until an authorized external reconciliation step establishes a safe,
    /// proven outcome.</para>
    ///
    /// <para><b>Definitive outcomes (AC#4):</b> any other non-null value (SUCCESS,
    /// BUSINESS_REJECTION, TECHNICAL_FAILURE, TRANSIENT_FAILURE) is a definitive outcome;
    /// the run orchestrator may follow the configured <c>actionCode</c> branch.</para>
    ///
    /// <para>This field is <see langword="null"/> for all non-service-invoke steps.</para>
    /// </summary>
    public ServiceOutcome? ServiceOutcome { get; init; }

    /// <summary>
    /// The <c>actionCode</c> returned by the service for a <c>service.invoke</c> step
    /// with a definitive outcome (BRW-US-015 AC#4).
    ///
    /// <para>Always <see langword="null"/> when <see cref="ServiceOutcome"/> is
    /// <see cref="Contracts.ServiceOutcome.UNKNOWN_OUTCOME"/> (AC#3 — no branch permitted).</para>
    /// </summary>
    public string? ServiceActionCode { get; init; }
}
