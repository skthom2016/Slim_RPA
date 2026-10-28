namespace Tracto.Desktop.Input;

/// <summary>
/// Delivers text and key-chord input to Windows UI Automation targets.
/// </summary>
public interface IKeyboardInputEngine
{
    /// <summary>
    /// Types <paramref name="options"/>.Text into the target element, choosing the best available strategy.
    /// </summary>
    Task<TypeResult> TypeAsync(TypeOptions options, CancellationToken ct = default);

    /// <summary>
    /// Presses a key combination described by <paramref name="chord"/> (e.g. "Ctrl+S", "Alt+F4").
    /// All modifier keys are guaranteed to be released even if an exception occurs.
    /// </summary>
    Task PressAsync(string chord, CancellationToken ct = default);

    /// <summary>
    /// Holds <paramref name="key"/> down for <paramref name="holdMs"/> milliseconds then releases it.
    /// The key-up is sent on normal completion, cancellation AND exceptions.
    /// </summary>
    Task KeyDownUpAsync(string key, int holdMs, CancellationToken ct = default);
}
