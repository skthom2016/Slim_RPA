using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Parameters for a mouse hover (move + optional dwell) action on a UIA-located element.
/// </summary>
public sealed record HoverOptions(
    ElementRef Target,
    int? OffsetX = null,
    int? OffsetY = null,
    int? DwellMs = null);
