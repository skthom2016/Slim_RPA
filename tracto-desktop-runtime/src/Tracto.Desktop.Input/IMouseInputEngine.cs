namespace Tracto.Desktop.Input;

/// <summary>
/// High-level mouse input operations that combine UIA element resolution,
/// offset validation and P/Invoke SendInput calls.
/// </summary>
public interface IMouseInputEngine
{
    /// <summary>
    /// Moves the cursor to the element center (plus optional offset) and sends a mouse button DOWN/UP pair.
    /// Validates that the offset, if supplied, stays within ±(Width/2, Height/2) of the element.
    /// Retry class: NEVER.
    /// </summary>
    Task ClickAsync(MouseClickOptions options, CancellationToken ct = default);

    /// <summary>
    /// Presses mouse button DOWN at source center, moves to target center and releases UP.
    /// On cancellation the button is released before OperationCanceledException is propagated.
    /// When the drop outcome cannot be confirmed, throws UnknownOutcomeException.
    /// </summary>
    Task DragAsync(DragOptions options, CancellationToken ct = default);

    /// <summary>
    /// Sends MOUSEEVENTF_WHEEL relative to the element centre.
    /// Retry class: NEVER (wheel movement is not idempotent; caller decides to retry).
    /// </summary>
    Task ScrollAsync(ScrollOptions options, CancellationToken ct = default);

    /// <summary>
    /// Moves the cursor to the element center (plus optional offset).
    /// When DwellMs is specified, waits that many milliseconds before returning.
    /// </summary>
    Task HoverAsync(HoverOptions options, CancellationToken ct = default);
}
