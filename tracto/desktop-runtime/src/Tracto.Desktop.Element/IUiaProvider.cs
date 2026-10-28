using System.Windows.Automation;

namespace Tracto.Desktop.Element;

/// <summary>
/// Abstracts AutomationElement tree traversal to enable deterministic unit testing
/// without requiring a live Windows UI Automation session or real WPF windows.
/// </summary>
public interface IUiaProvider
{
    /// <summary>
    /// Returns the first AutomationElement in the given scope that satisfies <paramref name="cond"/>,
    /// or null if no element matches.
    /// </summary>
    AutomationElement? FindFirst(TreeScope scope, Condition cond);

    /// <summary>
    /// Returns all AutomationElements in the given scope that satisfy <paramref name="cond"/>.
    /// </summary>
    AutomationElement[] FindAll(TreeScope scope, Condition cond);
}
