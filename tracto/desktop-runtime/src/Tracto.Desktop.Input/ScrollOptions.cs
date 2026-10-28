using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Parameters for a mouse wheel scroll action on a UIA-located element.
/// Retry class is NEVER — the orchestrator decides whether to scroll again.
/// </summary>
public sealed record ScrollOptions(
    ElementRef Target,
    int Clicks = 3,
    bool ScrollDown = true);
