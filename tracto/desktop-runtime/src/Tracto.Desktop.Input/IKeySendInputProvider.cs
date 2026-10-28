namespace Tracto.Desktop.Input;

/// <summary>
/// Abstraction over Win32 SendInput keyboard events so the keyboard engine
/// can be tested without real OS input injection.
/// </summary>
public interface IKeySendInputProvider
{
    /// <summary>Sends KEYEVENTF_KEYDOWN for the named key (e.g. "Ctrl", "S", "F4").</summary>
    void KeyDown(string key);

    /// <summary>Sends KEYEVENTF_KEYUP for the named key.</summary>
    void KeyUp(string key);

    /// <summary>Sends KEYEVENTF_UNICODE key-down for the given Unicode character.</summary>
    void CharDown(char ch);

    /// <summary>Sends KEYEVENTF_UNICODE key-up for the given Unicode character.</summary>
    void CharUp(char ch);

    /// <summary>Writes <paramref name="text"/> to the system clipboard.</summary>
    void SetClipboardText(string text);

    /// <summary>Sends Ctrl+V (paste).</summary>
    void SendPaste();
}
