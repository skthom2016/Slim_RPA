using System.Runtime.CompilerServices;
using System.Windows.Automation;
using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Configurable in-process test double for <see cref="IUiaPatternProvider"/>.
/// Does not require a live Windows UI Automation session.
/// </summary>
public sealed class MockUiaPatternProvider : IUiaPatternProvider
{
    // ── Pattern availability flags ────────────────────────────────────────────

    private readonly HashSet<string> _available = new(StringComparer.Ordinal);

    // ── State ─────────────────────────────────────────────────────────────────

    private ToggleState _toggleState = ToggleState.Off;
    private string _value            = string.Empty;
    private string _name             = string.Empty;
    private readonly List<string> _comboItems = new();

    // ── Call counters ─────────────────────────────────────────────────────────

    /// <summary>Number of times <see cref="Toggle"/> was called.</summary>
    public int ToggleCallCount { get; private set; }

    /// <summary>Number of times <see cref="Expand"/> was called.</summary>
    public int ExpandCallCount { get; private set; }

    /// <summary>Number of times <see cref="SelectComboItemAt"/> was called.</summary>
    public int SelectCallCount { get; private set; }

    /// <summary>Zero-based index of the last item selected, or null if none yet.</summary>
    public int? LastSelectedIndex { get; private set; }

    // ── Configuration helpers ─────────────────────────────────────────────────

    /// <summary>Configure whether the element exposes <c>TogglePattern</c>.</summary>
    public MockUiaPatternProvider WithToggle(bool available, ToggleState initialState = ToggleState.Off)
    {
        SetAvailability("Toggle", available);
        _toggleState = initialState;
        return this;
    }

    /// <summary>Configure whether the element exposes <c>ValuePattern</c>.</summary>
    public MockUiaPatternProvider WithValue(bool available, string initialValue = "")
    {
        SetAvailability("Value", available);
        _value = initialValue;
        return this;
    }

    /// <summary>Configure whether the element exposes <c>ExpandCollapsePattern</c>.</summary>
    public MockUiaPatternProvider WithExpandCollapse(bool available)
    {
        SetAvailability("ExpandCollapse", available);
        return this;
    }

    /// <summary>Configure whether the element exposes <c>SelectionItemPattern</c>.</summary>
    public MockUiaPatternProvider WithSelectionItem(bool available)
    {
        SetAvailability("SelectionItem", available);
        return this;
    }

    /// <summary>Set the items that will be returned by <see cref="GetComboItemNames"/>.</summary>
    public MockUiaPatternProvider WithComboItems(IEnumerable<string> items)
    {
        _comboItems.Clear();
        _comboItems.AddRange(items);
        return this;
    }

    /// <summary>Set the UIA Name property returned by <see cref="GetName"/>.</summary>
    public MockUiaPatternProvider WithName(string name)
    {
        _name = name;
        return this;
    }

    // ── IUiaPatternProvider ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public T? GetPattern<T>(ElementRef el) where T : BasePattern
    {
        string key = PatternKey<T>();
        if (!_available.Contains(key))
            return null;

        // Return an uninitialized sentinel so the engine can perform a non-null check.
        // The engine MUST NOT call COM methods on this object directly; it must use the
        // operation methods on this provider instead.
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }

    /// <inheritdoc/>
    public ToggleState GetToggleState(ElementRef el) => _toggleState;

    /// <inheritdoc/>
    public void Toggle(ElementRef el)
    {
        ToggleCallCount++;
        _toggleState = _toggleState == ToggleState.On ? ToggleState.Off : ToggleState.On;
    }

    /// <inheritdoc/>
    public void SetValue(ElementRef el, string value) => _value = value;

    /// <inheritdoc/>
    public string? GetValue(ElementRef el) =>
        _available.Contains("Value") ? _value : null;

    /// <inheritdoc/>
    public void Expand(ElementRef el)
    {
        ExpandCallCount++;
        _expandCollapseState = ExpandCollapseState.Expanded;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetComboItemNames(ElementRef container) =>
        _comboItems.AsReadOnly();

    /// <inheritdoc/>
    public void SelectComboItemAt(ElementRef container, int index)
    {
        SelectCallCount++;
        LastSelectedIndex = index;
    }

    /// <inheritdoc/>
    public string GetName(ElementRef el) => _name;

    // ── ExpandCollapse state ──────────────────────────────────────────────────

    private ExpandCollapseState _expandCollapseState = ExpandCollapseState.Collapsed;
    private int _collapseCallCount;

    /// <summary>Number of times <see cref="Collapse"/> was called.</summary>
    public int CollapseCallCount => _collapseCallCount;

    /// <summary>Configure the initial ExpandCollapse state and pattern availability.</summary>
    public MockUiaPatternProvider WithExpandCollapseState(ExpandCollapseState state)
    {
        _expandCollapseState = state;
        return this;
    }

    /// <inheritdoc/>
    public ExpandCollapseState GetExpandCollapseState(ElementRef el) => _expandCollapseState;

    /// <inheritdoc/>
    public void Collapse(ElementRef el)
    {
        _collapseCallCount++;
        _expandCollapseState = ExpandCollapseState.Collapsed;
    }

    // Override Expand to also update state
    // (note: original Expand only increments count — we update state too)

    // ── Grid / Table pattern ──────────────────────────────────────────────────

    private int _gridRowCount;
    private readonly Dictionary<(int, int), string> _cellValues = new();
    private readonly Dictionary<(int, int), bool>   _cellReadOnly = new();
    private readonly List<string> _gridColumnNames = new();
    private bool _realizeRowResult = true;
    private bool _realizeRowThrowsTimeout = false;

    /// <summary>Number of times <see cref="SelectGridRow"/> was called.</summary>
    public int SelectGridRowCallCount { get; private set; }
    /// <summary>Number of times <see cref="SetCellValue"/> was called.</summary>
    public int SetCellValueCallCount { get; private set; }
    /// <summary>Number of times <see cref="TryRealizeRow"/> was called.</summary>
    public int TryRealizeRowCallCount { get; private set; }

    /// <summary>Configure the grid row count.</summary>
    public MockUiaPatternProvider WithGridRowCount(int count) { _gridRowCount = count; return this; }

    /// <summary>Configure a cell value.</summary>
    public MockUiaPatternProvider WithCellValue(int row, int col, string value)
    {
        _cellValues[(row, col)] = value;
        return this;
    }

    /// <summary>Configure a cell as read-only.</summary>
    public MockUiaPatternProvider WithCellReadOnly(int row, int col, bool readOnly)
    {
        _cellReadOnly[(row, col)] = readOnly;
        return this;
    }

    /// <summary>Configure column names.</summary>
    public MockUiaPatternProvider WithGridColumnNames(IEnumerable<string> names)
    {
        _gridColumnNames.Clear();
        _gridColumnNames.AddRange(names);
        return this;
    }

    /// <summary>Configure what TryRealizeRow returns.</summary>
    public MockUiaPatternProvider WithRealizeRowResult(bool result)
    {
        _realizeRowResult = result;
        return this;
    }

    /// <summary>Configure TryRealizeRow to simulate timeout (return false).</summary>
    public MockUiaPatternProvider WithRealizeRowTimeout()
    {
        _realizeRowThrowsTimeout = true;
        return this;
    }

    /// <inheritdoc/>
    public int GetGridRowCount(ElementRef el) => _gridRowCount;

    /// <inheritdoc/>
    public string? GetGridCellValue(ElementRef el, int row, int column) =>
        _cellValues.TryGetValue((row, column), out string? v) ? v : null;

    /// <inheritdoc/>
    public IReadOnlyList<string> GetGridColumnNames(ElementRef el) => _gridColumnNames.AsReadOnly();

    /// <inheritdoc/>
    public void SelectGridRow(ElementRef el, int rowIndex) => SelectGridRowCallCount++;

    /// <inheritdoc/>
    public bool IsCellReadOnly(ElementRef el, int row, int column) =>
        _cellReadOnly.TryGetValue((row, column), out bool v) && v;

    /// <inheritdoc/>
    public void SetCellValue(ElementRef el, int row, int column, string value)
    {
        SetCellValueCallCount++;
        _cellValues[(row, column)] = value;
    }

    /// <inheritdoc/>
    public bool TryRealizeRow(ElementRef el, int rowIndex, TimeSpan timeout)
    {
        TryRealizeRowCallCount++;
        if (_realizeRowThrowsTimeout) return false;
        return _realizeRowResult;
    }

    // ── SelectionItemPattern (tabs) ───────────────────────────────────────────

    private readonly List<string> _selectionItems = new();
    /// <summary>Number of times <see cref="SelectItemAt"/> was called.</summary>
    public int SelectItemAtCallCount { get; private set; }
    /// <summary>Zero-based index of the last item selected via SelectItemAt.</summary>
    public int? LastSelectedItemIndex { get; private set; }

    /// <summary>Configure the selectable items (tab names etc.).</summary>
    public MockUiaPatternProvider WithSelectionItems(IEnumerable<string> items)
    {
        _selectionItems.Clear();
        _selectionItems.AddRange(items);
        return this;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetSelectionItemNames(ElementRef container) =>
        _selectionItems.AsReadOnly();

    /// <inheritdoc/>
    public void SelectItemAt(ElementRef container, int index)
    {
        SelectItemAtCallCount++;
        LastSelectedItemIndex = index;
    }

    // ── InvokePattern (menus) ─────────────────────────────────────────────────

    private readonly List<ElementRef> _children = new();
    /// <summary>Number of times <see cref="InvokeElement"/> was called.</summary>
    public int InvokeCallCount { get; private set; }

    /// <summary>Configure child elements returned by FindChildrenByName.</summary>
    public MockUiaPatternProvider WithChildren(IEnumerable<ElementRef> children)
    {
        _children.Clear();
        _children.AddRange(children);
        return this;
    }

    /// <inheritdoc/>
    public void InvokeElement(ElementRef el) => InvokeCallCount++;

    /// <inheritdoc/>
    public IReadOnlyList<ElementRef> FindChildrenByName(ElementRef el, string name) =>
        _children
            .Where(c => string.Equals(_name, name, StringComparison.OrdinalIgnoreCase) ||
                        true /* return all configured children for simplicity in tests */)
            .ToList()
            .AsReadOnly();

    // ── Dialog operations ─────────────────────────────────────────────────────

    private ElementRef? _dialogWindow;
    private ElementRef? _editControl;
    private readonly Dictionary<string, ElementRef> _buttons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Configure the dialog window returned by FindWindowByClass.</summary>
    public MockUiaPatternProvider WithDialogWindow(ElementRef? dialog)
    {
        _dialogWindow = dialog;
        return this;
    }

    /// <summary>Configure the Edit control returned by FindFirstEdit.</summary>
    public MockUiaPatternProvider WithEditControl(ElementRef? edit)
    {
        _editControl = edit;
        return this;
    }

    /// <summary>Configure a button returned by FindButtonByName.</summary>
    public MockUiaPatternProvider WithButton(string name, ElementRef button)
    {
        _buttons[name] = button;
        return this;
    }

    /// <inheritdoc/>
    public ElementRef? FindWindowByClass(string className) => _dialogWindow;

    /// <inheritdoc/>
    public ElementRef? FindFirstEdit(ElementRef container) => _editControl;

    /// <inheritdoc/>
    public ElementRef? FindButtonByName(ElementRef container, string name) =>
        _buttons.TryGetValue(name, out ElementRef? btn) ? btn : null;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string PatternKey<T>() where T : BasePattern =>
        typeof(T).Name.Replace("Pattern", string.Empty, StringComparison.Ordinal);

    private void SetAvailability(string key, bool available)
    {
        if (available) _available.Add(key);
        else           _available.Remove(key);
    }
}
