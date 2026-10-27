namespace Tracto.Desktop.Input;

/// <summary>
/// Test double for <see cref="IKeySendInputProvider"/>.
/// Records every keyboard event in order without sending real OS input.
/// </summary>
public sealed class MockKeySendInputProvider : IKeySendInputProvider
{
    private readonly List<KeyEvent> _events = new();

    /// <summary>All keyboard events recorded in call order.</summary>
    public IReadOnlyList<KeyEvent> Events => _events;

    public void KeyDown(string key) => _events.Add(new KeyEvent(KeyEventKind.KeyDown, key));
    public void KeyUp(string key)   => _events.Add(new KeyEvent(KeyEventKind.KeyUp, key));
    public void CharDown(char ch)   => _events.Add(new KeyEvent(KeyEventKind.CharDown, ch.ToString()));
    public void CharUp(char ch)     => _events.Add(new KeyEvent(KeyEventKind.CharUp, ch.ToString()));

    public void SetClipboardText(string text) =>
        _events.Add(new KeyEvent(KeyEventKind.ClipboardSet, text));

    public void SendPaste() =>
        _events.Add(new KeyEvent(KeyEventKind.Paste, "Ctrl+V"));

    public void Clear() => _events.Clear();
}

/// <summary>Kind of keyboard event recorded by <see cref="MockKeySendInputProvider"/>.</summary>
public enum KeyEventKind { KeyDown, KeyUp, CharDown, CharUp, ClipboardSet, Paste }

/// <summary>A single recorded keyboard event.</summary>
public sealed record KeyEvent(KeyEventKind Kind, string Label);
