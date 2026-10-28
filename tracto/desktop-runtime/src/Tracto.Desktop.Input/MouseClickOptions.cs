using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Parameters for a mouse click action on a UIA-located element.
/// OffsetX and OffsetY are control-relative; both must stay within ±(Width/2, Height/2).
/// </summary>
public sealed record MouseClickOptions(
    ElementRef Target,
    ClickButton Button = ClickButton.Left,
    int? OffsetX = null,
    int? OffsetY = null);
