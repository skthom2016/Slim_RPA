using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Provides high-level control interaction operations — set text fields, toggle checkboxes,
/// select radio buttons, choose combo items and set date pickers — using the best available
/// UIA pattern and recording the strategy used in <see cref="ControlValueResult"/>.
/// </summary>
public interface IControlInteractionEngine
{
    /// <summary>
    /// Sets the text value of <paramref name="el"/>.
    /// Tries ValuePattern first; falls back to clear + keystroke via <c>IKeyboardInputEngine</c>.
    /// </summary>
    Task<ControlValueResult> SetValueAsync(
        ElementRef        el,
        string            value,
        CancellationToken ct = default);

    /// <summary>
    /// Sets the checked state of a checkbox or radio button.
    /// Reads current TogglePattern state and toggles only when the state does not already match
    /// <paramref name="targetChecked"/>; if it already matches, returns <c>Changed=false</c>
    /// without sending any toggle event (idempotent).
    /// </summary>
    Task<ControlValueResult> SetCheckboxAsync(
        ElementRef        el,
        bool              targetChecked,
        CancellationToken ct = default);

    /// <summary>
    /// Selects a radio button by invoking SelectionItemPattern or TogglePattern.
    /// </summary>
    Task<ControlValueResult> SelectRadioAsync(
        ElementRef        el,
        CancellationToken ct = default);

    /// <summary>
    /// Selects a combo-box item by text and/or zero-based index.
    /// Expands the combo, finds matching items, and throws <see cref="Tracto.Desktop.Element.DesktopActionError"/>
    /// with code <c>"TARGET_AMBIGUOUS"</c> when more than one item matches
    /// <paramref name="optionText"/> and no <paramref name="index"/> is provided.
    /// </summary>
    Task<ControlValueResult> SelectComboItemAsync(
        ElementRef        el,
        string?           optionText,
        int?              index,
        CancellationToken ct = default);

    /// <summary>
    /// Sets a date-picker control to <paramref name="date"/>.
    /// Uses ValuePattern when available; otherwise navigates sub-fields via keyboard.
    /// </summary>
    Task<ControlValueResult> SetDateAsync(
        ElementRef        el,
        DateOnly          date,
        CancellationToken ct = default);
}
