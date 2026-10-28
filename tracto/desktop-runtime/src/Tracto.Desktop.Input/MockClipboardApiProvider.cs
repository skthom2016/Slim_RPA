namespace Tracto.Desktop.Input;

/// <summary>
/// Test double for <see cref="IClipboardApiProvider"/> that simulates clipboard contention
/// and tracks all calls for assertion in unit tests.
/// </summary>
public sealed class MockClipboardApiProvider : IClipboardApiProvider
{
    private string? _clipboardText;
    private bool _isOpen;
    private int _remainingFailures;

    /// <summary>Total number of <see cref="TryOpenClipboard"/> calls made.</summary>
    public int OpenAttempts { get; private set; }

    /// <summary>Total number of <see cref="CloseClipboard"/> calls made.</summary>
    public int CloseCallCount { get; private set; }

    /// <summary>Whether the clipboard is currently open.</summary>
    public bool IsOpen => _isOpen;

    /// <summary>Current text on the mock clipboard (visible to assertions).</summary>
    public string? ClipboardText
    {
        get => _clipboardText;
        set => _clipboardText = value;
    }

    /// <summary>
    /// Configures the provider to fail <paramref name="count"/> consecutive
    /// <see cref="TryOpenClipboard"/> calls before succeeding, simulating contention.
    /// </summary>
    public void SimulateContentionFor(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        _remainingFailures = count;
    }

    /// <inheritdoc/>
    public bool TryOpenClipboard(IntPtr owner)
    {
        OpenAttempts++;
        if (_remainingFailures > 0)
        {
            _remainingFailures--;
            return false;
        }
        _isOpen = true;
        return true;
    }

    /// <inheritdoc/>
    public void CloseClipboard()
    {
        CloseCallCount++;
        _isOpen = false;
    }

    /// <inheritdoc/>
    public string? GetText() => _clipboardText;

    /// <inheritdoc/>
    public void SetText(string text)
    {
        _clipboardText = text;
    }
}
