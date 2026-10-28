using System.Windows.Automation;
using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Production implementation of <see cref="ITreeInteractionEngine"/>.
/// Uses <see cref="IUiaPatternProvider"/> to expand and collapse WPF/WinForms
/// TreeView nodes via <c>ExpandCollapsePattern</c>.
/// </summary>
public sealed class TreeInteractionEngine : ITreeInteractionEngine
{
    private readonly IUiaPatternProvider _patterns;

    public TreeInteractionEngine(IUiaPatternProvider patterns)
    {
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
    }

    // ── ExpandAsync ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task ExpandAsync(
        ElementRef        el,
        bool              expanded,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(el);

        if (_patterns.GetPattern<ExpandCollapsePattern>(el) is null)
            throw new DesktopActionError("ENVIRONMENT",
                "desktop.expandCollapsePatternUnavailable");

        ExpandCollapseState currentState = _patterns.GetExpandCollapseState(el);

        if (expanded)
        {
            // Idempotent: already expanded — do not send a second ExpandCollapse event.
            if (currentState == ExpandCollapseState.Expanded)
                return Task.CompletedTask;

            _patterns.Expand(el);
        }
        else
        {
            // Idempotent: already collapsed — do not send a second ExpandCollapse event.
            if (currentState == ExpandCollapseState.Collapsed)
                return Task.CompletedTask;

            _patterns.Collapse(el);
        }

        return Task.CompletedTask;
    }
}
