using Tracto.Desktop.Element;

namespace Tracto.Desktop.Recovery;

/// <summary>
/// Verifies that an element's state satisfies a declared postcondition predicate after an action.
/// A failed assertion means the step FAILED — not SUCCEEDED — because the expected outcome was not confirmed.
/// </summary>
public sealed class PostconditionAssertion
{
    private readonly IElementStateChecker _checker;

    public PostconditionAssertion(IElementStateChecker checker)
    {
        _checker = checker ?? throw new ArgumentNullException(nameof(checker));
    }

    /// <summary>
    /// Reads the current state of <paramref name="el"/> and evaluates <paramref name="predicate"/>.
    /// Throws <see cref="DesktopActionError"/> when the predicate is not satisfied.
    /// </summary>
    /// <exception cref="DesktopActionError">
    /// ErrorCategory=ENVIRONMENT, DetailCode=desktop.postconditionFailed — predicate returned false.
    /// </exception>
    public async Task AssertAsync(ElementRef el, Func<ElementStateResult, bool> predicate, CancellationToken ct)
    {
        var state = await _checker.CheckAsync(el, ct).ConfigureAwait(false);

        if (!predicate(state))
            throw new DesktopActionError("ENVIRONMENT", "desktop.postconditionFailed");
    }
}
