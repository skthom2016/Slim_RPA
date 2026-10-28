using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Parameters for a mouse drag action from one UIA-located element to another.
/// </summary>
public sealed record DragOptions(
    ElementRef Source,
    ElementRef Target,
    int DragDurationMs = 300);
