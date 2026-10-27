using System.Windows.Automation;
using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Abstracts UIA pattern access and pattern-level operations for testability.
/// Production implementations delegate to the live Windows UI Automation COM layer;
/// test doubles configure availability and record operation calls without a real desktop.
/// </summary>
public interface IUiaPatternProvider
{
    /// <summary>
    /// Returns the pattern instance for <typeparamref name="T"/> if the element supports it,
    /// or <c>null</c> when the pattern is not available.
    /// </summary>
    T? GetPattern<T>(ElementRef el) where T : BasePattern;

    // ── TogglePattern operations ──────────────────────────────────────────────

    /// <summary>Returns the current <see cref="ToggleState"/> of the element.</summary>
    ToggleState GetToggleState(ElementRef el);

    /// <summary>
    /// Calls <c>TogglePattern.Toggle()</c> on the element — must be called only when
    /// <see cref="GetPattern{T}"/> returns non-null for <c>TogglePattern</c>.
    /// </summary>
    void Toggle(ElementRef el);

    // ── ValuePattern operations ───────────────────────────────────────────────

    /// <summary>
    /// Calls <c>ValuePattern.SetValue(value)</c> on the element — must be called only when
    /// <see cref="GetPattern{T}"/> returns non-null for <c>ValuePattern</c>.
    /// </summary>
    void SetValue(ElementRef el, string value);

    /// <summary>
    /// Reads the current value via <c>ValuePattern.Current.Value</c>.
    /// Returns <c>null</c> when ValuePattern is unavailable.
    /// </summary>
    string? GetValue(ElementRef el);

    // ── ExpandCollapsePattern operations ─────────────────────────────────────

    /// <summary>
    /// Calls <c>ExpandCollapsePattern.Expand()</c> — must be called only when the pattern is available.
    /// </summary>
    void Expand(ElementRef el);

    // ── ComboBox / SelectionItemPattern operations ───────────────────────────

    /// <summary>
    /// Returns the Name of each item exposed as a child of the container element.
    /// Used to locate items for <c>SelectComboItemAsync</c>.
    /// </summary>
    IReadOnlyList<string> GetComboItemNames(ElementRef container);

    /// <summary>
    /// Selects the combo item at zero-based <paramref name="index"/> within the container.
    /// </summary>
    void SelectComboItemAt(ElementRef container, int index);

    // ── Name / accessible-text fallback ──────────────────────────────────────

    /// <summary>Returns the UIA Name property of the element; empty string when unavailable.</summary>
    string GetName(ElementRef el);

    // ── ExpandCollapsePattern state ───────────────────────────────────────────

    /// <summary>
    /// Returns the current <see cref="ExpandCollapseState"/> of the element.
    /// Must be called only when <c>ExpandCollapsePattern</c> is available.
    /// </summary>
    ExpandCollapseState GetExpandCollapseState(ElementRef el);

    /// <summary>
    /// Calls <c>ExpandCollapsePattern.Collapse()</c> — must be called only when the pattern is available.
    /// </summary>
    void Collapse(ElementRef el);

    // ── Grid / Table pattern operations ──────────────────────────────────────

    /// <summary>
    /// Returns the number of rows in the grid element.
    /// Returns 0 when the element does not support GridPattern.
    /// </summary>
    int GetGridRowCount(ElementRef el);

    /// <summary>
    /// Returns the text of the cell at (<paramref name="row"/>, <paramref name="column"/>).
    /// Returns null when the pattern is unavailable or the cell cannot be read.
    /// </summary>
    string? GetGridCellValue(ElementRef el, int row, int column);

    /// <summary>
    /// Returns the column names (headers) for the grid element.
    /// Returns an empty list when the element does not expose column headers.
    /// </summary>
    IReadOnlyList<string> GetGridColumnNames(ElementRef el);

    /// <summary>
    /// Selects the grid row at zero-based <paramref name="rowIndex"/>.
    /// </summary>
    void SelectGridRow(ElementRef el, int rowIndex);

    /// <summary>
    /// Returns true when the cell at (<paramref name="row"/>, <paramref name="column"/>) is read-only.
    /// </summary>
    bool IsCellReadOnly(ElementRef el, int row, int column);

    /// <summary>
    /// Enters edit mode on the cell at (<paramref name="row"/>, <paramref name="column"/>) and
    /// sets <paramref name="value"/>. Throws <see cref="Tracto.Desktop.Element.DesktopActionError"/>
    /// with code <c>"ENVIRONMENT"</c> when the cell is read-only.
    /// </summary>
    void SetCellValue(ElementRef el, int row, int column, string value);

    /// <summary>
    /// Attempts to realize a virtualized row at <paramref name="rowIndex"/> within
    /// <paramref name="timeout"/>. Returns true on success, false on timeout / unsupported.
    /// </summary>
    bool TryRealizeRow(ElementRef el, int rowIndex, TimeSpan timeout);

    // ── SelectionItemPattern operations (tabs / list items) ──────────────────

    /// <summary>
    /// Returns the names of each selectable item in a container (tab strip, list).
    /// </summary>
    IReadOnlyList<string> GetSelectionItemNames(ElementRef container);

    /// <summary>
    /// Calls <c>SelectionItemPattern.Select()</c> on the item at <paramref name="index"/>
    /// within the container.
    /// </summary>
    void SelectItemAt(ElementRef container, int index);

    // ── InvokePattern operations (menus) ─────────────────────────────────────

    /// <summary>
    /// Calls <c>InvokePattern.Invoke()</c> on the element.
    /// </summary>
    void InvokeElement(ElementRef el);

    /// <summary>
    /// Returns the child elements of <paramref name="el"/> whose Name matches
    /// <paramref name="name"/> (case-insensitive).
    /// </summary>
    IReadOnlyList<ElementRef> FindChildrenByName(ElementRef el, string name);

    // ── Dialog operations ─────────────────────────────────────────────────────

    /// <summary>
    /// Finds the topmost dialog window with the given Win32 class name (e.g. "32770").
    /// Returns null when no such window is found.
    /// </summary>
    ElementRef? FindWindowByClass(string className);

    /// <summary>
    /// Returns the first Edit control inside <paramref name="container"/>.
    /// Returns null when none is found.
    /// </summary>
    ElementRef? FindFirstEdit(ElementRef container);

    /// <summary>
    /// Finds a button inside <paramref name="container"/> whose Name matches
    /// <paramref name="name"/> (case-insensitive). Returns null when not found.
    /// </summary>
    ElementRef? FindButtonByName(ElementRef container, string name);
}
