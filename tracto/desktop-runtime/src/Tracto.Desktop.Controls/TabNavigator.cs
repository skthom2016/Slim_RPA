using System.Windows.Automation;
using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Production implementation of <see cref="ITabNavigator"/>.
/// Locates a tab item by name within a tab strip and invokes
/// <c>SelectionItemPattern.Select()</c> on the matching item.
/// </summary>
public sealed class TabNavigator : ITabNavigator
{
    private readonly IUiaPatternProvider _patterns;

    public TabNavigator(IUiaPatternProvider patterns)
    {
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
    }

    /// <inheritdoc/>
    public Task SelectTabAsync(
        ElementRef        tabStrip,
        TabSelector       tab,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tabStrip);
        ArgumentNullException.ThrowIfNull(tab);

        IReadOnlyList<string> items = _patterns.GetSelectionItemNames(tabStrip);

        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i], tab.TabName, StringComparison.OrdinalIgnoreCase))
            {
                // SelectionItemPattern.Select() — idempotent by UIA contract when
                // the item is already selected, so we call unconditionally here and
                // rely on the pattern not dispatching a redundant event.
                _patterns.SelectItemAt(tabStrip, i);
                return Task.CompletedTask;
            }
        }

        throw new DesktopActionError("TARGET_NOT_FOUND",
            $"desktop.tabNotFound: '{tab.TabName}'");
    }
}
