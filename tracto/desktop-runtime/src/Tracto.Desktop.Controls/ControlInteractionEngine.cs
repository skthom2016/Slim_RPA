using System.Globalization;
using System.Windows.Automation;
using Tracto.Desktop.Element;
using Tracto.Desktop.Input;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Production implementation of <see cref="IControlInteractionEngine"/>.
/// Uses <see cref="IUiaPatternProvider"/> for pattern queries and operations,
/// and falls back to <see cref="IKeyboardInputEngine"/> for keystroke entry.
/// </summary>
public sealed class ControlInteractionEngine : IControlInteractionEngine
{
    private readonly IUiaPatternProvider  _patterns;
    private readonly IKeyboardInputEngine _keyboard;
    private readonly ControlReadback      _readback;

    public ControlInteractionEngine(
        IUiaPatternProvider  patterns,
        IKeyboardInputEngine keyboard)
    {
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
        _keyboard = keyboard ?? throw new ArgumentNullException(nameof(keyboard));
        _readback = new ControlReadback(patterns);
    }

    // ── SetValueAsync ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ControlValueResult> SetValueAsync(
        ElementRef        el,
        string            value,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(el);
        ArgumentNullException.ThrowIfNull(value);

        if (_patterns.GetPattern<ValuePattern>(el) is not null)
        {
            _patterns.SetValue(el, value);
            string? readback = await _readback.ReadValueAsync(el, ct).ConfigureAwait(false);
            return new ControlValueResult(Changed: true, Strategy: "ValuePattern", ReadbackValue: readback);
        }

        // Keystroke fallback: select-all + delete, then type.
        await _keyboard.PressAsync("Ctrl+A", ct).ConfigureAwait(false);
        await _keyboard.PressAsync("Delete", ct).ConfigureAwait(false);
        await _keyboard.TypeAsync(new TypeOptions(el, value), ct).ConfigureAwait(false);

        string? readbackKbd = await _readback.ReadValueAsync(el, ct).ConfigureAwait(false);
        return new ControlValueResult(Changed: true, Strategy: "Keystroke", ReadbackValue: readbackKbd);
    }

    // ── SetCheckboxAsync ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<ControlValueResult> SetCheckboxAsync(
        ElementRef        el,
        bool              targetChecked,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(el);

        if (_patterns.GetPattern<TogglePattern>(el) is null)
            throw new DesktopActionError("UNSUPPORTED_CONTROL",
                "desktop.togglePatternUnavailable");

        ToggleState current = _patterns.GetToggleState(el);
        bool        isChecked = current == ToggleState.On;

        // Idempotent: already in the desired state — do nothing.
        if (isChecked == targetChecked)
        {
            return Task.FromResult(
                new ControlValueResult(Changed: false, Strategy: "Toggle", ReadbackValue: current.ToString()));
        }

        // Toggle once and verify the resulting state.
        _patterns.Toggle(el);
        ToggleState after = _patterns.GetToggleState(el);

        return Task.FromResult(
            new ControlValueResult(
                Changed:       true,
                Strategy:      "Toggle",
                ReadbackValue: after.ToString()));
    }

    // ── SelectRadioAsync ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<ControlValueResult> SelectRadioAsync(
        ElementRef        el,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(el);

        // Prefer SelectionItemPattern; fall back to Toggle.
        if (_patterns.GetPattern<SelectionItemPattern>(el) is not null)
        {
            // SelectionItemPattern.Select() is done via provider.
            // We model "select" as selecting at index 0 of a single-item container.
            _patterns.SelectComboItemAt(el, 0);
            return Task.FromResult(
                new ControlValueResult(Changed: true, Strategy: "SelectionItemPattern", ReadbackValue: null));
        }

        if (_patterns.GetPattern<TogglePattern>(el) is not null)
        {
            ToggleState current = _patterns.GetToggleState(el);
            if (current != ToggleState.On)
                _patterns.Toggle(el);
            ToggleState after = _patterns.GetToggleState(el);
            return Task.FromResult(
                new ControlValueResult(Changed: current != ToggleState.On,
                    Strategy: "Toggle", ReadbackValue: after.ToString()));
        }

        throw new DesktopActionError("UNSUPPORTED_CONTROL",
            "desktop.radioPatternUnavailable");
    }

    // ── SelectComboItemAsync ──────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<ControlValueResult> SelectComboItemAsync(
        ElementRef        el,
        string?           optionText,
        int?              index,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(el);

        if (_patterns.GetPattern<ExpandCollapsePattern>(el) is null)
            throw new DesktopActionError("UNSUPPORTED_CONTROL",
                "desktop.expandCollapsePatternUnavailable");

        // Expand the combo to realise its children.
        _patterns.Expand(el);

        IReadOnlyList<string> items = _patterns.GetComboItemNames(el);

        // Select by explicit index when provided.
        if (index.HasValue)
        {
            if (index.Value < 0 || index.Value >= items.Count)
                throw new DesktopActionError("TARGET_NOT_FOUND",
                    $"desktop.comboIndexOutOfRange: {index.Value} (count={items.Count})");

            _patterns.SelectComboItemAt(el, index.Value);
            string selected = items[index.Value];
            return Task.FromResult(
                new ControlValueResult(Changed: true, Strategy: "SelectionItemPattern",
                    ReadbackValue: selected));
        }

        // Select by text — require unique match.
        if (optionText is null)
            throw new ArgumentException("optionText and index cannot both be null.", nameof(optionText));

        List<int> matches = new();
        for (int i = 0; i < items.Count; i++)
        {
            if (string.Equals(items[i], optionText, StringComparison.OrdinalIgnoreCase))
                matches.Add(i);
        }

        if (matches.Count == 0)
            throw new DesktopActionError("TARGET_NOT_FOUND",
                $"desktop.comboItemNotFound: '{optionText}'");

        if (matches.Count > 1)
            throw new DesktopActionError("TARGET_AMBIGUOUS",
                $"desktop.comboItemAmbiguous: '{optionText}' matches {matches.Count} items");

        _patterns.SelectComboItemAt(el, matches[0]);
        return Task.FromResult(
            new ControlValueResult(Changed: true, Strategy: "SelectionItemPattern",
                ReadbackValue: items[matches[0]]));
    }

    // ── SetDateAsync ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ControlValueResult> SetDateAsync(
        ElementRef        el,
        DateOnly          date,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(el);

        if (_patterns.GetPattern<ValuePattern>(el) is not null)
        {
            // Use the en-US short-date format as a safe, locale-invariant representation.
            string formatted = date.ToString("M/d/yyyy", CultureInfo.InvariantCulture);
            _patterns.SetValue(el, formatted);
            return new ControlValueResult(
                Changed: true, Strategy: "ValuePattern", ReadbackValue: formatted);
        }

        // Keyboard procedure: Tab between month, day and year sub-fields.
        string month = date.Month.ToString(CultureInfo.InvariantCulture);
        string day   = date.Day.ToString(CultureInfo.InvariantCulture);
        string year  = date.Year.ToString(CultureInfo.InvariantCulture);

        await _keyboard.TypeAsync(new TypeOptions(el, month), ct).ConfigureAwait(false);
        await _keyboard.PressAsync("Tab", ct).ConfigureAwait(false);
        await _keyboard.TypeAsync(new TypeOptions(el, day), ct).ConfigureAwait(false);
        await _keyboard.PressAsync("Tab", ct).ConfigureAwait(false);
        await _keyboard.TypeAsync(new TypeOptions(el, year), ct).ConfigureAwait(false);

        return new ControlValueResult(
            Changed: true, Strategy: "Keystroke",
            ReadbackValue: date.ToString("M/d/yyyy", CultureInfo.InvariantCulture));
    }
}
