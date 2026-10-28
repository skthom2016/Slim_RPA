namespace Tracto.Desktop.Input;

/// <summary>
/// Describes which mechanism was used (or should be tried) to deliver text to a target element.
/// </summary>
public enum TypeStrategy
{
    /// <summary>UIA ValuePattern.SetValue() — fastest and most reliable for editable controls.</summary>
    ValuePattern,

    /// <summary>SendInput KEYBDINPUT path — character-by-character synthetic keystrokes.</summary>
    Keystroke,

    /// <summary>Write to clipboard then send Ctrl+V — explicit opt-in only; never selected automatically.</summary>
    ClipboardPaste,
}
