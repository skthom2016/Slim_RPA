using Tracto.Desktop.Element;

namespace Tracto.Desktop.Recovery;

/// <summary>
/// Polls an element selector until a state predicate is satisfied or the timeout elapses.
/// </summary>
public interface IElementWaiter
{
    /// <summary>
    /// Polls <paramref name="sel"/> at <see cref="WaitOptions.PollIntervalMs"/> intervals
    /// until the <see cref="WaitOptions.Condition"/> is met or <see cref="WaitOptions.TimeoutMs"/> expires.
    /// </summary>
    /// <exception cref="DesktopActionError">
    /// ErrorCategory=TIMEOUT when the condition is not met within the timeout.
    /// The exception message includes the last observed element state.
    /// </exception>
    Task<WaitResult> WaitAsync(ElementSelector sel, WaitOptions opts, CancellationToken ct);
}
