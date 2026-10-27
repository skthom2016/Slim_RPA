namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Detects unexpected modal windows positioned above a target application window.
/// When <see cref="CheckForUnexpectedModal"/> returns a result with
/// <see cref="ModalBlockResult.IsBlocked"/> = <c>true</c>, the calling action MUST be withheld.
/// </summary>
public sealed class ModalBlocker
{
    private readonly IWindowEnumerator _enumerator;

    // Maximum title length surfaced in evidence to avoid leaking long window titles
    // that could embed secret data captured by the window manager.
    private const int MaxTitleLength = 128;

    public ModalBlocker(IWindowEnumerator enumerator)
    {
        _enumerator = enumerator ?? throw new ArgumentNullException(nameof(enumerator));
    }

    /// <summary>
    /// Checks whether an unexpected window is covering <paramref name="expectedWindow"/>.
    /// </summary>
    /// <param name="expectedWindow">HWND of the window that should be in the foreground.</param>
    /// <returns>
    /// A <see cref="ModalBlockResult"/> describing whether the action must be withheld.
    /// </returns>
    public ModalBlockResult CheckForUnexpectedModal(IntPtr expectedWindow)
    {
        if (_enumerator.HasUnexpectedModal(expectedWindow))
        {
            // Find the topmost unexpected window to include in evidence.
            var allWindows = _enumerator.EnumerateTopLevel();
            var blocking = allWindows
                .Where(w => w.Handle != expectedWindow)
                .FirstOrDefault();

            string? sanitizedTitle = null;
            string evidence;

            if (blocking is not null)
            {
                sanitizedTitle = Sanitize(blocking.Title);
                evidence = $"Unexpected window '{sanitizedTitle}' (class: '{blocking.ClassName}', " +
                           $"PID: {blocking.ProcessId}) is positioned above the target window " +
                           $"(HWND: 0x{expectedWindow:X}). Action withheld per REC-003.";
            }
            else
            {
                evidence = $"An unexpected window is above the target window (HWND: 0x{expectedWindow:X}). " +
                           "Action withheld per REC-003.";
            }

            return new ModalBlockResult(IsBlocked: true, ModalTitle: sanitizedTitle, Evidence: evidence);
        }

        return new ModalBlockResult(
            IsBlocked: false,
            ModalTitle: null,
            Evidence: $"No unexpected modal detected above HWND 0x{expectedWindow:X}.");
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Truncates the title so that extremely long titles do not fill logs.</summary>
    private static string Sanitize(string title)
    {
        if (string.IsNullOrEmpty(title)) return string.Empty;
        return title.Length > MaxTitleLength ? title[..MaxTitleLength] + "…" : title;
    }
}
