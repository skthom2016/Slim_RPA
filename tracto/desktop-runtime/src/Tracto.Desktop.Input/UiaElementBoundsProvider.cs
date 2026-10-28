using System.Windows;
using System.Windows.Automation;
using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Production IElementBoundsProvider that reads BoundingRectangle from the live UIA tree.
/// </summary>
public sealed class UiaElementBoundsProvider : IElementBoundsProvider
{
    public static readonly UiaElementBoundsProvider Instance = new();

    public Rect GetBounds(ElementRef elementRef)
    {
        ArgumentNullException.ThrowIfNull(elementRef);
        try
        {
            var rect = elementRef.AutomationElement.Current.BoundingRectangle;
            if (rect.IsEmpty || rect == Rect.Empty)
                throw new InvalidOperationException(
                    $"Element '{elementRef.Selector}' has no valid bounding rectangle.");
            return rect;
        }
        catch (ElementNotAvailableException ex)
        {
            throw new InvalidOperationException(
                $"Element '{elementRef.Selector}' is no longer available in the UIA tree.", ex);
        }
    }
}
