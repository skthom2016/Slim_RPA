using System.Windows.Automation;
using FluentAssertions;
using Tracto.Desktop.Controls;
using Tracto.Desktop.Element;
using Tracto.Desktop.Input;
using Xunit;

namespace Tracto.Desktop.Controls.Tests;

/// <summary>
/// DT-13 and DT-14 advanced control interaction unit tests.
/// All tests use mock providers — no live Windows desktop required.
/// </summary>
public sealed class AdvancedControlsTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ElementRef MakeElement()
    {
        System.Windows.Automation.AutomationElement element;
        try
        {
            element = System.Windows.Automation.AutomationElement.RootElement;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            element = (System.Windows.Automation.AutomationElement)
                System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                    typeof(System.Windows.Automation.AutomationElement));
        }
        return new(element, new ElementSelector { AutomationId = "test-element" });
    }

    // ── DT-13-AC04: TreeView Expand when already Expanded → no second call ───

    /// <summary>
    /// When a tree node is already Expanded and ExpandAsync is called with expanded=true,
    /// no second ExpandCollapsePattern call must be sent (idempotent).
    /// </summary>
    [Fact]
    public async Task TreeExpandAsync_AlreadyExpanded_NoSecondExpandCall()
    {
        var provider = new MockUiaPatternProvider()
            .WithExpandCollapse(available: true)
            .WithExpandCollapseState(ExpandCollapseState.Expanded);

        var engine = new TreeInteractionEngine(provider);
        var el     = MakeElement();

        await engine.ExpandAsync(el, expanded: true);

        // The Expand call count must remain 0 — the node was already expanded.
        provider.ExpandCallCount.Should().Be(0,
            "expand must be idempotent when the node is already Expanded");
    }

    // ── DT-13-AC08: Read-only grid cell → EditCellAsync → ENVIRONMENT error ──

    /// <summary>
    /// EditCellAsync on a read-only grid cell must throw DesktopActionError("ENVIRONMENT")
    /// and must not call SetCellValue.
    /// </summary>
    [Fact]
    public async Task GridEditCellAsync_ReadOnlyCell_ThrowsEnvironmentError()
    {
        var provider = new MockUiaPatternProvider()
            .WithCellReadOnly(row: 0, col: 0, readOnly: true);

        var engine = new GridInteractionEngine(provider);
        var el     = MakeElement();

        Func<Task> act = () => engine.EditCellAsync(el, row: 0, column: 0, newValue: "value");

        await act.Should().ThrowAsync<DesktopActionError>()
            .Where(e => e.ErrorCode == "ENVIRONMENT");

        provider.SetCellValueCallCount.Should().Be(0,
            "no SetCellValue must be called on a read-only cell");
    }

    // ── DT-13-AC06: VirtualizedItemPattern.Realize times out → TIMEOUT thrown ─

    /// <summary>
    /// ScrollToRowAsync must throw DesktopActionError("TIMEOUT") when TryRealizeRow
    /// returns false (simulating a timeout). The error must not be suppressed.
    /// </summary>
    [Fact]
    public async Task GridScrollToRowAsync_RealizeTimesOut_ThrowsTimeoutError()
    {
        var provider = new MockUiaPatternProvider()
            .WithGridRowCount(1)
            .WithCellValue(row: 0, col: 0, value: "target")
            .WithGridColumnNames(new[] { "ID" })
            .WithRealizeRowTimeout(); // TryRealizeRow returns false

        var engine    = new GridInteractionEngine(provider);
        var el        = MakeElement();
        var selector  = new GridRowSelector(MatchCellValue: "target", ColumnName: "ID", RowIndex: null);

        Func<Task> act = () => engine.ScrollToRowAsync(el, selector, TimeSpan.FromSeconds(5));

        await act.Should().ThrowAsync<DesktopActionError>()
            .Where(e => e.ErrorCode == "TIMEOUT",
                   "timeout during realization must not be suppressed");
    }

    // ── DT-14-AC01: SelectTabAsync finds tab by name → SelectItemAt called ───

    /// <summary>
    /// SelectTabAsync must locate the tab named "Settings" (case-insensitive) and call
    /// SelectionItemPattern.Select() exactly once on the matching item.
    /// </summary>
    [Fact]
    public async Task TabSelectAsync_FindsTabByName_SelectCalled()
    {
        var provider = new MockUiaPatternProvider()
            .WithSelectionItems(new[] { "General", "Settings", "Advanced" });

        var navigator = new TabNavigator(provider);
        var stripEl   = MakeElement();

        await navigator.SelectTabAsync(stripEl, new TabSelector("Settings"));

        provider.SelectItemAtCallCount.Should().Be(1,
            "SelectionItemPattern.Select must be called exactly once");
        provider.LastSelectedItemIndex.Should().Be(1,
            "Settings is at index 1 in the configured tab list");
    }

    // ── DT-14-AC03: ConfirmDialogHandler DenyAsync → POLICY_DENIED ───────────

    /// <summary>
    /// DenyAsync with a button name that is not on the approved deny list must throw
    /// DesktopActionError("POLICY_DENIED") before any UI interaction occurs.
    /// </summary>
    [Fact]
    public async Task ConfirmDialogDenyAsync_UnapprovedButton_ThrowsPolicyDenied()
    {
        var provider = new MockUiaPatternProvider();
        var handler  = new ConfirmDialogHandler(provider);
        var dialogEl = MakeElement();

        // "Delete" is not in the approved deny list.
        Func<Task> act = () => handler.DenyAsync(dialogEl, buttonName: "Delete");

        await act.Should().ThrowAsync<DesktopActionError>()
            .Where(e => e.ErrorCode == "POLICY_DENIED",
                   "unapproved dismiss must raise POLICY_DENIED");

        provider.InvokeCallCount.Should().Be(0,
            "no button must be invoked when the policy check fails");
    }

    // ── Bonus: TreeCollapse idempotent when already Collapsed ────────────────

    [Fact]
    public async Task TreeExpandAsync_AlreadyCollapsed_NoCollapseCall()
    {
        var provider = new MockUiaPatternProvider()
            .WithExpandCollapse(available: true)
            .WithExpandCollapseState(ExpandCollapseState.Collapsed);

        var engine = new TreeInteractionEngine(provider);

        await engine.ExpandAsync(MakeElement(), expanded: false);

        provider.CollapseCallCount.Should().Be(0,
            "collapse must be idempotent when the node is already Collapsed");
    }

    // ── Bonus: SelectRowAsync by cell content ─────────────────────────────────

    [Fact]
    public async Task GridSelectRowAsync_ByContent_SelectsCorrectRow()
    {
        var provider = new MockUiaPatternProvider()
            .WithGridRowCount(3)
            .WithGridColumnNames(new[] { "Name", "Value" })
            .WithCellValue(0, 0, "Alice")
            .WithCellValue(1, 0, "Bob")
            .WithCellValue(2, 0, "Carol");

        var engine   = new GridInteractionEngine(provider);
        var selector = new GridRowSelector(MatchCellValue: "Bob", ColumnName: "Name", RowIndex: null);

        await engine.SelectRowAsync(MakeElement(), selector);

        provider.SelectGridRowCallCount.Should().Be(1);
    }

    // ── Bonus: TabNavigator throws TARGET_NOT_FOUND for missing tab ───────────

    [Fact]
    public async Task TabSelectAsync_MissingTab_ThrowsTargetNotFound()
    {
        var provider  = new MockUiaPatternProvider()
            .WithSelectionItems(new[] { "General", "Advanced" });
        var navigator = new TabNavigator(provider);

        Func<Task> act = () => navigator.SelectTabAsync(MakeElement(), new TabSelector("Missing"));

        await act.Should().ThrowAsync<DesktopActionError>()
            .Where(e => e.ErrorCode == "TARGET_NOT_FOUND");
    }
}
