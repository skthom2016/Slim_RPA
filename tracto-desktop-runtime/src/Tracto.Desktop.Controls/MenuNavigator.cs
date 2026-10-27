using System.Windows.Automation;
using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Production implementation of <see cref="IMenuNavigator"/>.
/// Walks a menu hierarchy by name, invoking each step via <c>InvokePattern.Invoke()</c>.
/// When a step exposes <c>ExpandCollapsePattern</c> instead of <c>InvokePattern</c> (e.g.
/// a submenu header) the engine expands it to reveal the next level.
/// </summary>
public sealed class MenuNavigator : IMenuNavigator
{
    private readonly IUiaPatternProvider _patterns;

    public MenuNavigator(IUiaPatternProvider patterns)
    {
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
    }

    /// <inheritdoc/>
    public async Task NavigateAsync(
        ElementRef        menuRoot,
        MenuPath          path,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(menuRoot);
        ArgumentNullException.ThrowIfNull(path);

        ElementRef current = menuRoot;

        foreach (string step in path.Steps)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<ElementRef> children = _patterns.FindChildrenByName(current, step);

            if (children.Count == 0)
                throw new DesktopActionError("TARGET_NOT_FOUND",
                    $"desktop.menuItemNotFound: '{step}'");

            ElementRef item = children[0];

            // Prefer InvokePattern; expand via ExpandCollapsePattern if needed.
            if (_patterns.GetPattern<ExpandCollapsePattern>(item) is not null &&
                _patterns.GetPattern<InvokePattern>(item) is null)
            {
                _patterns.Expand(item);
            }
            else
            {
                _patterns.InvokeElement(item);
            }

            current = item;
            await Task.Yield(); // yield to allow UIA events to settle between steps
        }
    }
}
