using System.Windows;
using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Resolves the bounding rectangle for a located element.
/// Abstracted so that unit tests can supply fake bounds without real UIA.
/// </summary>
public interface IElementBoundsProvider
{
    /// <summary>Returns the screen bounding rectangle for <paramref name="elementRef"/>.</summary>
    Rect GetBounds(ElementRef elementRef);
}
