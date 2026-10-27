using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Production <see cref="IClipboardEngine"/> implementation.
/// Uses <see cref="ContentionRetryPolicy"/> for open-contention recovery and
/// optionally preserves the OS clipboard around paste operations.
/// </summary>
public sealed class WindowsClipboardEngine : IClipboardEngine
{
    private readonly IClipboardApiProvider _api;
    private readonly ContentionRetryPolicy _retryPolicy;
    private readonly IKeyInputSimulator     _keyInput;

    // Tracks whether the clipboard is open within this engine instance.
    private volatile bool _isOpen;

    public WindowsClipboardEngine(
        IClipboardApiProvider api,
        ContentionRetryPolicy retryPolicy,
        IKeyInputSimulator keyInput)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        ArgumentNullException.ThrowIfNull(keyInput);

        _api         = api;
        _retryPolicy = retryPolicy;
        _keyInput    = keyInput;
    }

    // ── IClipboardEngine ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    /// <remarks>
    /// Flow: ContentionRetryPolicy.TryOpen -> select-all on element -> Ctrl+C ->
    ///       GetText -> CloseClipboard in finally.
    /// Retry class: TRANSIENT_SAFE (only the open attempt is retried).
    /// </remarks>
    public async Task<ClipboardRef> CopyAsync(
        ElementRef target,
        string refName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(refName);

        bool opened = await _retryPolicy.TryOpenAsync(IntPtr.Zero, ct).ConfigureAwait(false);
        if (!opened)
            throw new InvalidOperationException(
                $"Could not open clipboard after contention retry for ref '{refName}'.");

        _isOpen = true;
        try
        {
            // Select all text in the target element and copy to clipboard.
            await _keyInput.SelectAllAsync(target, ct).ConfigureAwait(false);
            await _keyInput.SendCopyAsync(ct).ConfigureAwait(false);

            // Read from clipboard; the text is in the OS clipboard, accessible via the provider.
            _ = _api.GetText();

            // ClipboardRef is a token only — text stays in ClearableBuffer managed by callers.
            return new ClipboardRef(refName);
        }
        finally
        {
            _api.CloseClipboard();
            _isOpen = false;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Flow:
    ///   BestEffortRestore: snapshot current clipboard text before paste.
    ///   Open clipboard -> SetText -> CloseClipboard -> focus element -> Ctrl+V.
    ///   BestEffortRestore: restore snapshot in finally.
    /// Retry class: NEVER — no implicit retry inside this method.
    /// Uncertain completion -> <see cref="UnknownOutcomeException"/>.
    /// </remarks>
    public async Task PasteAsync(
        ClearableBuffer content,
        ElementRef target,
        ClipboardPastePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        // target may be null in test scenarios where IKeyInputSimulator is a no-op.

        string? snapshot = null;

        // Best-effort snapshot of existing clipboard before we overwrite it.
        if (policy == ClipboardPastePolicy.BestEffortRestore)
        {
            try
            {
                bool snapshotOpened = await _retryPolicy.TryOpenAsync(IntPtr.Zero, ct).ConfigureAwait(false);
                if (snapshotOpened)
                {
                    _isOpen = true;
                    try
                    {
                        snapshot = _api.GetText();
                    }
                    finally
                    {
                        _api.CloseClipboard();
                        _isOpen = false;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // best-effort: intentionally swallow non-cancellation errors
            catch
            {
                snapshot = null;
            }
#pragma warning restore CA1031
        }

        // Write the paste content to the OS clipboard.
        bool pasteOpened = await _retryPolicy.TryOpenAsync(IntPtr.Zero, ct).ConfigureAwait(false);
        if (!pasteOpened)
            throw new InvalidOperationException("Could not open clipboard for paste.");

        _isOpen = true;
        try
        {
            _api.SetText(new string(content.Span));
            _api.CloseClipboard();
            _isOpen = false;

            // Inject paste keystroke — retry class NEVER.
            await _keyInput.SendPasteAsync(target, ct).ConfigureAwait(false);

            // Paste is irreversible with no reliable readback confirmation.
            // Per DT-10-AC02: uncertain paste -> UnknownOutcomeException.
            throw new UnknownOutcomeException("action.commitUncertain");
        }
        finally
        {
            // Guarantee handle released even when an exception is thrown mid-copy.
            if (_isOpen)
            {
                _api.CloseClipboard();
                _isOpen = false;
            }

            // Best-effort restore of the original clipboard text.
            if (policy == ClipboardPastePolicy.BestEffortRestore && snapshot is not null)
            {
                try
                {
                    bool restoreOpened = _api.TryOpenClipboard(IntPtr.Zero);
                    if (restoreOpened)
                    {
                        try
                        {
                            _api.SetText(snapshot);
                        }
                        finally
                        {
                            _api.CloseClipboard();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
#pragma warning disable CA1031
                catch
                {
                    // Restore failure is non-fatal; production code would emit a trace here.
                }
#pragma warning restore CA1031
            }
        }
    }

    /// <inheritdoc/>
    public void ForceCloseHandle()
    {
        // Idempotent cleanup for cancellation paths — safe to call even when not open.
        _api.CloseClipboard();
        _isOpen = false;
    }
}
