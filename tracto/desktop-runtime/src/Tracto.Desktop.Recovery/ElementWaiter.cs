using Tracto.Desktop.Element;

namespace Tracto.Desktop.Recovery;

/// <summary>
/// Polls <see cref="IElementFinder"/> and <see cref="IElementStateChecker"/> at a configured
/// interval until a <see cref="WaitCondition"/> is met or the timeout expires.
/// </summary>
public sealed class ElementWaiter : IElementWaiter
{
    private readonly IElementFinder _finder;
    private readonly IElementStateChecker _checker;
    private readonly ITimer _timer;

    public ElementWaiter(IElementFinder finder, IElementStateChecker checker, ITimer timer)
    {
        _finder = finder ?? throw new ArgumentNullException(nameof(finder));
        _checker = checker ?? throw new ArgumentNullException(nameof(checker));
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
    }

    /// <inheritdoc />
    public async Task<WaitResult> WaitAsync(ElementSelector sel, WaitOptions opts, CancellationToken ct)
    {
        long elapsed = 0;
        string? lastState = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            (bool met, string state) = await EvaluateConditionAsync(sel, opts.Condition, ct).ConfigureAwait(false);
            lastState = state;

            if (met)
            {
                return new WaitResult
                {
                    Resolved = true,
                    LastObservedState = lastState,
                    ElapsedMs = elapsed,
                };
            }

            // Check timeout before sleeping.
            if (elapsed >= opts.TimeoutMs)
                break;

            await _timer.DelayAsync(opts.PollIntervalMs, ct).ConfigureAwait(false);
            elapsed += opts.PollIntervalMs;
        }

        throw new DesktopActionError(
            "TIMEOUT",
            $"desktop.waitTimeout — last observed state: {lastState ?? "unknown"} after {elapsed}ms");
    }

    private async Task<(bool Met, string State)> EvaluateConditionAsync(
        ElementSelector sel, WaitCondition condition, CancellationToken ct)
    {
        if (condition == WaitCondition.NotExists)
        {
            try
            {
                await _finder.FindAsync(sel, ElementFindOptions.Default, ct).ConfigureAwait(false);
                return (false, "exists");
            }
            catch (DesktopActionError e) when (e.ErrorCode == "TARGET_NOT_FOUND")
            {
                return (true, "not_found");
            }
        }

        try
        {
            var el = await _finder.FindAsync(sel, ElementFindOptions.Default, ct).ConfigureAwait(false);
            var state = await _checker.CheckAsync(el, ct).ConfigureAwait(false);
            string stateStr = $"visible={state.IsVisible},enabled={state.IsEnabled},hasProvider={state.HasProvider}";

            bool met = condition switch
            {
                WaitCondition.Exists => true,
                WaitCondition.Visible => state.IsVisible,
                WaitCondition.Enabled => state.IsEnabled,
                _ => false,
            };

            return (met, stateStr);
        }
        catch (DesktopActionError e) when (e.ErrorCode == "TARGET_NOT_FOUND")
        {
            return (false, "not_found");
        }
    }
}
