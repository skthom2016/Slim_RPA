using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// High-level clipboard operations: copy from a UI element and paste into a UI element,
/// with contention recovery and optional clipboard restoration.
/// </summary>
public interface IClipboardEngine
{
    /// <summary>
    /// Copies the text from <paramref name="target"/> into the OS clipboard and returns a
    /// <see cref="ClipboardRef"/> token.  The resolved text is stored in the engine's internal
    /// <see cref="ClearableBuffer"/>; it is NOT a field on <see cref="ClipboardRef"/>.
    /// </summary>
    /// <remarks>Retry class: TRANSIENT_SAFE (only the clipboard open is retried).</remarks>
    Task<ClipboardRef> CopyAsync(ElementRef target, string refName, CancellationToken ct = default);

    /// <summary>
    /// Pastes <paramref name="content"/> into <paramref name="target"/>.
    /// If <paramref name="policy"/> is <see cref="ClipboardPastePolicy.BestEffortRestore"/>
    /// the current OS clipboard text is captured before the paste and restored afterwards.
    /// </summary>
    /// <remarks>
    /// Retry class: NEVER.  A second paste must be a separate explicit call.
    /// Uncertain completion (no readback confirmation) throws <see cref="UnknownOutcomeException"/>.
    /// </remarks>
    Task PasteAsync(
        ClearableBuffer content,
        ElementRef target,
        ClipboardPastePolicy policy,
        CancellationToken ct = default);

    /// <summary>
    /// Closes the clipboard handle unconditionally, even when called outside the normal flow.
    /// Intended for cancellation and exception-cleanup paths (DT-17-AC03).
    /// </summary>
    void ForceCloseHandle();
}
