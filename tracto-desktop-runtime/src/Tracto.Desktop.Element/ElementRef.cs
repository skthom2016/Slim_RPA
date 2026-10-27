using System.Windows.Automation;

namespace Tracto.Desktop.Element;

/// <summary>
/// Holds a located AutomationElement alongside the selector that produced it.
/// </summary>
public sealed class ElementRef
{
    private readonly AutomationElement _automationElement;

    public ElementRef(AutomationElement automationElement, ElementSelector selector)
    {
        _automationElement = automationElement ?? throw new ArgumentNullException(nameof(automationElement));
        Selector = selector ?? throw new ArgumentNullException(nameof(selector));
    }

    public ElementSelector Selector { get; }

    public AutomationElement AutomationElement => _automationElement;

    /// <summary>
    /// Returns true when the underlying AutomationElement is still available in the UIA tree.
    /// Catches ElementNotAvailableException to detect stale references.
    /// </summary>
    public bool IsAlive
    {
        get
        {
            try
            {
                // Accessing ProcessId forces a live UIA round-trip; throws if the element is gone.
                return _automationElement.Current.ProcessId > 0;
            }
            catch (ElementNotAvailableException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
