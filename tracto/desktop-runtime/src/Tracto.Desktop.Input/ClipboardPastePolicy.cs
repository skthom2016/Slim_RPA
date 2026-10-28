namespace Tracto.Desktop.Input;

/// <summary>
/// Controls whether the OS clipboard contents are saved and restored around a paste operation.
/// </summary>
public enum ClipboardPastePolicy
{
    /// <summary>
    /// Do not attempt to snapshot or restore the OS clipboard.
    /// The paste operation replaces whatever was on the clipboard.
    /// </summary>
    NoRestore,

    /// <summary>
    /// Capture the OS clipboard text before the paste and restore it afterwards.
    /// This is best-effort: if the clipboard holds a non-text format the capture is skipped
    /// and no restore is attempted. Failure to restore is non-fatal and is traced.
    /// </summary>
    BestEffortRestore,
}
