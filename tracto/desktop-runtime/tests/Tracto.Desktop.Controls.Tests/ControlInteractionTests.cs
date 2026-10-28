using System.Windows.Automation;
using FluentAssertions;
using Tracto.Desktop.Controls;
using Tracto.Desktop.Element;
using Tracto.Desktop.Input;
using Xunit;

namespace Tracto.Desktop.Controls.Tests;

/// <summary>DT-12 — Core Control Interaction Surface unit tests.</summary>
public sealed class ControlInteractionTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates an ElementRef backed by the UIA desktop root or an uninitialised sentinel.</summary>
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

    private static ControlInteractionEngine BuildEngine(
        MockUiaPatternProvider provider,
        MockKeyboardEngine?    keyboard = null) =>
        new(provider, keyboard ?? new MockKeyboardEngine());

    // ── DT-12-AC01: SetValueAsync — ValuePattern available ───────────────────

    [Fact]
    public async Task SetValueAsync_ValuePatternAvailable_StrategyIsValuePattern()
    {
        var provider = new MockUiaPatternProvider().WithValue(available: true, initialValue: "");
        var engine   = BuildEngine(provider);

        var result = await engine.SetValueAsync(MakeElement(), "hello world");

        result.Strategy.Should().Be("ValuePattern");
        result.Changed.Should().BeTrue();
    }

    // ── DT-12-AC01: SetValueAsync — no ValuePattern (keystroke fallback) ─────

    [Fact]
    public async Task SetValueAsync_NoValuePattern_StrategyIsKeystroke()
    {
        var keyboard = new MockKeyboardEngine();
        var provider = new MockUiaPatternProvider().WithValue(available: false);
        var engine   = BuildEngine(provider, keyboard);

        var result = await engine.SetValueAsync(MakeElement(), "abc");

        result.Strategy.Should().Be("Keystroke");
        result.Changed.Should().BeTrue();
        // PressAsync called twice (Ctrl+A, Delete) and TypeAsync once.
        keyboard.PressCallCount.Should().Be(2);
        keyboard.TypeCallCount.Should().Be(1);
    }

    // ── DT-12-AC02: SetCheckboxAsync — already in target state → Changed=false ─

    [Fact]
    public async Task SetCheckboxAsync_AlreadyChecked_ReturnsFalseAndNoToggleSent()
    {
        // Checkbox is currently checked (On).
        var provider = new MockUiaPatternProvider()
            .WithToggle(available: true, initialState: ToggleState.On);
        var engine = BuildEngine(provider);

        var result = await engine.SetCheckboxAsync(MakeElement(), targetChecked: true);

        result.Changed.Should().BeFalse();
        result.Strategy.Should().Be("Toggle");
        // CRITICAL: zero Toggle calls — idempotent no-op.
        provider.ToggleCallCount.Should().Be(0);
    }

    [Fact]
    public async Task SetCheckboxAsync_AlreadyUnchecked_ReturnsFalseAndNoToggleSent()
    {
        var provider = new MockUiaPatternProvider()
            .WithToggle(available: true, initialState: ToggleState.Off);
        var engine = BuildEngine(provider);

        var result = await engine.SetCheckboxAsync(MakeElement(), targetChecked: false);

        result.Changed.Should().BeFalse();
        provider.ToggleCallCount.Should().Be(0);
    }

    // ── DT-12-AC03: SetCheckboxAsync — not in target state → Changed=true; Toggle called once

    [Fact]
    public async Task SetCheckboxAsync_NotInTargetState_TogglesExactlyOnce()
    {
        // Checkbox starts unchecked; we want it checked.
        var provider = new MockUiaPatternProvider()
            .WithToggle(available: true, initialState: ToggleState.Off);
        var engine = BuildEngine(provider);

        var result = await engine.SetCheckboxAsync(MakeElement(), targetChecked: true);

        result.Changed.Should().BeTrue();
        result.Strategy.Should().Be("Toggle");
        provider.ToggleCallCount.Should().Be(1);
        result.ReadbackValue.Should().Be(ToggleState.On.ToString());
    }

    [Fact]
    public async Task SetCheckboxAsync_WasChecked_UnchecksWithExactlyOneToggle()
    {
        // Checkbox starts checked; we want it unchecked.
        var provider = new MockUiaPatternProvider()
            .WithToggle(available: true, initialState: ToggleState.On);
        var engine = BuildEngine(provider);

        var result = await engine.SetCheckboxAsync(MakeElement(), targetChecked: false);

        result.Changed.Should().BeTrue();
        provider.ToggleCallCount.Should().Be(1);
        result.ReadbackValue.Should().Be(ToggleState.Off.ToString());
    }

    // ── DT-12-AC06: SelectComboItemAsync — two identical items, no index → TARGET_AMBIGUOUS

    [Fact]
    public async Task SelectComboItemAsync_TwoIdenticalItemsNoIndex_ThrowsTargetAmbiguous()
    {
        var provider = new MockUiaPatternProvider()
            .WithExpandCollapse(available: true)
            .WithComboItems(new[] { "Unknown", "Unknown", "Option C" });
        var engine = BuildEngine(provider);

        Func<Task> act = async () =>
            await engine.SelectComboItemAsync(MakeElement(), optionText: "Unknown", index: null);

        var ex = await act.Should().ThrowAsync<DesktopActionError>();
        ex.Which.ErrorCode.Should().Be("TARGET_AMBIGUOUS");
        // Must not have selected anything.
        provider.SelectCallCount.Should().Be(0);
    }

    // ── DT-12-AC05: SelectComboItemAsync — unique match → selection confirmed ─

    [Fact]
    public async Task SelectComboItemAsync_UniqueMatch_SelectsCorrectItem()
    {
        var provider = new MockUiaPatternProvider()
            .WithExpandCollapse(available: true)
            .WithSelectionItem(available: true)
            .WithComboItems(new[] { "Option A", "Option B", "Option C" });
        var engine = BuildEngine(provider);

        var result = await engine.SelectComboItemAsync(
            MakeElement(), optionText: "Option B", index: null);

        result.Changed.Should().BeTrue();
        result.Strategy.Should().Be("SelectionItemPattern");
        result.ReadbackValue.Should().Be("Option B");
        provider.SelectCallCount.Should().Be(1);
        provider.LastSelectedIndex.Should().Be(1); // zero-based index of "Option B"
        provider.ExpandCallCount.Should().Be(1);
    }

    // ── DT-12-AC05 variant: selection by explicit index ───────────────────────

    [Fact]
    public async Task SelectComboItemAsync_ByIndex_SelectsItemAtThatIndex()
    {
        var provider = new MockUiaPatternProvider()
            .WithExpandCollapse(available: true)
            .WithComboItems(new[] { "Alpha", "Beta", "Gamma" });
        var engine = BuildEngine(provider);

        var result = await engine.SelectComboItemAsync(
            MakeElement(), optionText: null, index: 2);

        result.Changed.Should().BeTrue();
        result.ReadbackValue.Should().Be("Gamma");
        provider.LastSelectedIndex.Should().Be(2);
    }

    // ── Guard: ExpandCollapsePattern must be present ──────────────────────────

    [Fact]
    public async Task SelectComboItemAsync_NoExpandPattern_ThrowsUnsupportedControl()
    {
        var provider = new MockUiaPatternProvider(); // no patterns configured
        var engine   = BuildEngine(provider);

        Func<Task> act = async () =>
            await engine.SelectComboItemAsync(MakeElement(), optionText: "X", index: null);

        var ex = await act.Should().ThrowAsync<DesktopActionError>();
        ex.Which.ErrorCode.Should().Be("UNSUPPORTED_CONTROL");
    }

    // ── Guard: TogglePattern must be present for SetCheckboxAsync ─────────────

    [Fact]
    public async Task SetCheckboxAsync_NoTogglePattern_ThrowsUnsupportedControl()
    {
        var provider = new MockUiaPatternProvider(); // no toggle pattern
        var engine   = BuildEngine(provider);

        Func<Task> act = async () =>
            await engine.SetCheckboxAsync(MakeElement(), targetChecked: true);

        var ex = await act.Should().ThrowAsync<DesktopActionError>();
        ex.Which.ErrorCode.Should().Be("UNSUPPORTED_CONTROL");
    }

    // ── ControlReadback ───────────────────────────────────────────────────────

    [Fact]
    public async Task ControlReadback_ValuePatternAvailable_ReturnsPatternValue()
    {
        var provider = new MockUiaPatternProvider()
            .WithValue(available: true, initialValue: "ReadMe");
        var readback = new ControlReadback(provider);

        string? value = await readback.ReadValueAsync(MakeElement());

        value.Should().Be("ReadMe");
    }

    [Fact]
    public async Task ControlReadback_NoValuePattern_FallsBackToName()
    {
        var provider = new MockUiaPatternProvider()
            .WithValue(available: false)
            .WithName("MyButton");
        var readback = new ControlReadback(provider);

        string? value = await readback.ReadValueAsync(MakeElement());

        value.Should().Be("MyButton");
    }

    [Fact]
    public async Task ControlReadback_NeitherPatternNorName_ReturnsNull()
    {
        var provider = new MockUiaPatternProvider(); // nothing configured
        var readback = new ControlReadback(provider);

        string? value = await readback.ReadValueAsync(MakeElement());

        value.Should().BeNull();
    }
}

// ── Test doubles ──────────────────────────────────────────────────────────────

/// <summary>
/// Minimal recording stub for <see cref="IKeyboardInputEngine"/> used by Controls tests.
/// Does not require a live UIA session.
/// </summary>
internal sealed class MockKeyboardEngine : IKeyboardInputEngine
{
    public int TypeCallCount  { get; private set; }
    public int PressCallCount { get; private set; }

    public Task<TypeResult> TypeAsync(TypeOptions options, CancellationToken ct = default)
    {
        TypeCallCount++;
        return Task.FromResult(new TypeResult(TypeStrategy.Keystroke, Succeeded: true, Evidence: null));
    }

    public Task PressAsync(string chord, CancellationToken ct = default)
    {
        PressCallCount++;
        return Task.CompletedTask;
    }

    public Task KeyDownUpAsync(string key, int holdMs, CancellationToken ct = default) =>
        Task.CompletedTask;
}
