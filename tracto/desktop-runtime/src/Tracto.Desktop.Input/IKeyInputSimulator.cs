using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Abstracts the keyboard-shortcut injection needed by clipboard operations,
/// enabling the clipboard engine to be tested without a real Windows UIA tree.
/// </summary>
public interface IKeyInputSimulator
{
    /// <summary>Focuses <paramref name="element"/> and sends Ctrl+A (select all).</summary>
    Task SelectAllAsync(ElementRef element, CancellationToken ct);

    /// <summary>Sends Ctrl+C (copy) to the currently focused element.</summary>
    Task SendCopyAsync(CancellationToken ct);

    /// <summary>Focuses <paramref name="element"/> and sends Ctrl+V (paste).</summary>
    Task SendPasteAsync(ElementRef element, CancellationToken ct);
}
