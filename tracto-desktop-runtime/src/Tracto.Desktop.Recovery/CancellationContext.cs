namespace Tracto.Desktop.Recovery;

/// <summary>
/// Captures the runtime state that must be cleaned up when a job is cancelled,
/// times out, or terminates unexpectedly (DT-17).
/// </summary>
/// <param name="JobId">Identifier of the job whose resources must be released.</param>
/// <param name="AttemptId">Identifier of the execution attempt being cancelled.</param>
/// <param name="HeldModifierKeys">
///   Names of modifier keys (e.g. "Ctrl", "Shift") that are currently held down
///   and must receive KEYEVENTF_KEYUP events during cleanup.
/// </param>
/// <param name="IsMouseButtonDown">
///   <c>true</c> when a mouse button (left) is logically held; a MOUSEEVENTF_LEFTUP
///   must be sent during cleanup.
/// </param>
/// <param name="ClipboardHandleOpen">
///   <c>true</c> when the clipboard is open (between OpenClipboard and CloseClipboard);
///   <c>IClipboardEngine.ForceCloseHandle</c> must be called during cleanup.
/// </param>
public sealed record CancellationContext(
    string JobId,
    string AttemptId,
    IReadOnlyList<string> HeldModifierKeys,
    bool IsMouseButtonDown,
    bool ClipboardHandleOpen);
