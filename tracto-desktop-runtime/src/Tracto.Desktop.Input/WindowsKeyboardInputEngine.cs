using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Windows implementation of <see cref="IKeyboardInputEngine"/>.
/// Supports ValuePattern (UIA), Keystroke (SendInput KEYBDINPUT) and ClipboardPaste strategies.
/// </summary>
public sealed class WindowsKeyboardInputEngine : IKeyboardInputEngine
{
    private readonly IKeySendInputProvider _keyProvider;
    private readonly IValuePatternProvider _valuePatternProvider;
    private readonly IClock _clock;

    /// <summary>
    /// Keys that map to virtual-key names recognised by <see cref="IKeySendInputProvider"/>.
    /// </summary>
    private static readonly HashSet<string> ModifierKeys =
        new(StringComparer.OrdinalIgnoreCase) { "Ctrl", "Alt", "Shift", "Win" };

    public WindowsKeyboardInputEngine(
        IKeySendInputProvider keyProvider,
        IValuePatternProvider valuePatternProvider,
        IClock clock)
    {
        _keyProvider          = keyProvider          ?? throw new ArgumentNullException(nameof(keyProvider));
        _valuePatternProvider = valuePatternProvider ?? throw new ArgumentNullException(nameof(valuePatternProvider));
        _clock                = clock                ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc/>
    public async Task<TypeResult> TypeAsync(TypeOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Clipboard paste is explicit opt-in; bypass all other strategies.
        if (options.Preferred == TypeStrategy.ClipboardPaste)
        {
            _keyProvider.SetClipboardText(options.Text);
            _keyProvider.SendPaste();
            return new TypeResult(TypeStrategy.ClipboardPaste, Succeeded: true, Evidence: null);
        }

        // Step 1: Ensure the target element has keyboard focus.
        bool hasFocus = _valuePatternProvider.EnsureKeyboardFocus(options.Target, timeoutMs: 2000);
        if (!hasFocus)
        {
            return new TypeResult(
                TypeStrategy.Keystroke,
                Succeeded: false,
                Evidence: "errorCategory=ENVIRONMENT;detailCode=keyboard.focusTimeout");
        }

        // Step 2: Try ValuePattern.SetValue when it is available (and not overridden away).
        if (options.Preferred != TypeStrategy.Keystroke
            && _valuePatternProvider.HasValuePattern(options.Target))
        {
            _valuePatternProvider.SetValue(options.Target, options.Text);
            return new TypeResult(TypeStrategy.ValuePattern, Succeeded: true, Evidence: null);
        }

        // Step 3: Keystroke fallback — send KEYBDINPUT for each character.
        ct.ThrowIfCancellationRequested();
        foreach (char ch in options.Text)
        {
            ct.ThrowIfCancellationRequested();
            _keyProvider.CharDown(ch);
            _keyProvider.CharUp(ch);
        }

        return new TypeResult(TypeStrategy.Keystroke, Succeeded: true, Evidence: null);
    }

    /// <inheritdoc/>
    public Task PressAsync(string chord, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chord)) throw new ArgumentException("chord must not be empty", nameof(chord));

        // Parse "Ctrl+S" → ["Ctrl", "S"]
        string[] parts = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new ArgumentException($"Invalid chord: '{chord}'", nameof(chord));

        string[] modifiers = parts[..^1];
        string   mainKey   = parts[^1];

        // Hold all modifiers down, press main key, then release everything in reverse.
        foreach (string mod in modifiers)
            _keyProvider.KeyDown(mod);

        _keyProvider.KeyDown(mainKey);
        _keyProvider.KeyUp(mainKey);

        // Release modifiers in reverse order (innermost first).
        for (int i = modifiers.Length - 1; i >= 0; i--)
            _keyProvider.KeyUp(modifiers[i]);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task KeyDownUpAsync(string key, int holdMs, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("key must not be empty", nameof(key));
        if (holdMs < 0)                     throw new ArgumentOutOfRangeException(nameof(holdMs));

        _keyProvider.KeyDown(key);
        try
        {
            await _clock.DelayAsync(holdMs, ct).ConfigureAwait(false);
        }
        finally
        {
            // KeyUp is guaranteed even on cancellation or exception.
            _keyProvider.KeyUp(key);
        }
    }
}
