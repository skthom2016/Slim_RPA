using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Worker;

namespace TractoRuntime.Browser.Crash;

/// <summary>
/// Pure-logic classifier that determines whether a browser crash should be recorded as
/// <see cref="RunState.FAILED"/> or <see cref="RunState.UNKNOWN"/>.
///
/// Classification rules (BRW-US-004 BRE-004):
/// <list type="bullet">
///   <item>
///     <term>FAILED</term>
///     <description>
///       No effectful action was dispatched before the crash — the state is deterministic
///       (AC#3: crash before any irreversible side effect → FAILED, not UNKNOWN).
///     </description>
///   </item>
///   <item>
///     <term>UNKNOWN</term>
///     <description>
///       At least one effectful action was dispatched but its outcome was never settled —
///       the browser crashed while the action's result was indeterminate (AC#4).
///     </description>
///   </item>
///   <item>
///     <term>FAILED</term>
///     <description>
///       Every dispatched effectful action has a corresponding settlement entry — all side
///       effects were confirmed before the crash, so the state is deterministic (AC#4).
///     </description>
///   </item>
/// </list>
///
/// Journal entry conventions used by this classifier:
/// <list type="bullet">
///   <item>An effectful action is dispatched with <c>action = "{name}"</c>, e.g.
///         <c>"form.submit"</c>, <c>"click"</c>, <c>"navigate"</c> etc.</item>
///   <item>Its confirmed outcome is recorded with <c>action = "{name}.settled"</c>,
///         e.g. <c>"form.submit.settled"</c>.</item>
///   <item>A dispatched entry without a matching settlement entry for the same
///         <c>StepId</c> means the action's outcome is indeterminate.</item>
/// </list>
/// </summary>
public static class CrashStateClassifier
{
    // Exact set of effectful action names — actions that may commit irreversible side effects.
    // Kept in sync with ReconciliationService._effectfulActions.
    private static readonly HashSet<string> EffectfulActionNames =
    [
        "form.submit",
        "click",
        "navigate",
        "file.download",
        "file.upload",
        "service.invoke"
    ];

    /// <summary>
    /// Classifies the run terminal state from <paramref name="entries"/> recorded up to the crash.
    /// </summary>
    /// <param name="entries">
    /// Journal entries in chronological order.  Must not be <c>null</c>.
    /// </param>
    /// <returns>
    /// <see cref="RunState.FAILED"/> or <see cref="RunState.UNKNOWN"/>.
    /// </returns>
    public static RunState Classify(IReadOnlyList<JournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var dispatchedStepIds = new HashSet<string>(StringComparer.Ordinal);
        var settledStepIds    = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (EffectfulActionNames.Contains(entry.Action))
            {
                // Effectful action dispatched to the browser — side effect may have occurred
                dispatchedStepIds.Add(entry.StepId);
            }
            else if (IsSettlementEntry(entry.Action))
            {
                // Confirmed outcome received — this step's result is established
                settledStepIds.Add(entry.StepId);
            }
        }

        // AC#3 / AC#4 (no effectful action path): crash before any side effect → FAILED
        if (dispatchedStepIds.Count == 0)
            return RunState.FAILED;

        // AC#4 (uncertain outcome path): any dispatched action without settlement → UNKNOWN
        foreach (var stepId in dispatchedStepIds)
        {
            if (!settledStepIds.Contains(stepId))
                return RunState.UNKNOWN;
        }

        // AC#4 (all settled path): every side effect has a confirmed outcome → FAILED
        return RunState.FAILED;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="actionName"/> is a settlement marker
    /// (e.g. <c>"click.settled"</c>) for one of the known effectful actions.
    /// </summary>
    private static bool IsSettlementEntry(string actionName)
    {
        const string Suffix = ".settled";
        if (!actionName.EndsWith(Suffix, StringComparison.Ordinal))
            return false;

        var prefix = actionName[..^Suffix.Length];
        return EffectfulActionNames.Contains(prefix);
    }
}
