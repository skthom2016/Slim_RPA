using FluentAssertions;
using Tracto.Desktop.Input;
using Xunit;

namespace Tracto.Desktop.Input.Tests;

/// <summary>DT-09 — Keyboard Input unit tests.</summary>
public sealed class KeyboardTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static WindowsKeyboardInputEngine BuildEngine(
        MockKeySendInputProvider keyProvider,
        FakeValuePatternProvider valuePatternProvider,
        FakeClock? clock = null) =>
        new(keyProvider, valuePatternProvider, clock ?? new FakeClock());

    // ── KeyDownUpAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task KeyDownUpAsync_Normal_RecordsDownThenUp()
    {
        var keys   = new MockKeySendInputProvider();
        var engine = BuildEngine(keys, new FakeValuePatternProvider(hasValuePattern: false));

        // Use a zero-hold so the real SystemClock or FakeClock completes immediately.
        var fakeClock = new FakeClock();
        var engineWithClock = new WindowsKeyboardInputEngine(keys, new FakeValuePatternProvider(false), fakeClock);

        var task = engineWithClock.KeyDownUpAsync("A", holdMs: 100);
        fakeClock.AdvanceMs(100);
        await task;

        keys.Events.Should().HaveCount(2);
        keys.Events[0].Should().Be(new KeyEvent(KeyEventKind.KeyDown, "A"));
        keys.Events[1].Should().Be(new KeyEvent(KeyEventKind.KeyUp,   "A"));
    }

    [Fact]
    public async Task KeyDownUpAsync_CancelledDuringHold_StillSendsKeyUp()
    {
        var keys      = new MockKeySendInputProvider();
        var fakeClock = new FakeClock();
        var engine    = new WindowsKeyboardInputEngine(keys, new FakeValuePatternProvider(false), fakeClock);
        var cts       = new CancellationTokenSource();

        var task = engine.KeyDownUpAsync("Ctrl", holdMs: 5000, ct: cts.Token);

        // Cancel BEFORE advancing the clock, simulating mid-hold cancellation.
        cts.Cancel();

        // The task should throw OperationCanceledException (or its subtype TaskCanceledException).
        await FluentActions.Invoking(async () => await task)
            .Should().ThrowAsync<OperationCanceledException>();

        // KeyUp must still have been sent despite cancellation.
        keys.Events.Should().Contain(e => e.Kind == KeyEventKind.KeyDown && e.Label == "Ctrl");
        keys.Events.Should().Contain(e => e.Kind == KeyEventKind.KeyUp   && e.Label == "Ctrl");
        keys.Events[0].Kind.Should().Be(KeyEventKind.KeyDown);
        keys.Events[^1].Kind.Should().Be(KeyEventKind.KeyUp);
    }

    // ── TypeAsync — ValuePattern path ─────────────────────────────────────────

    [Fact]
    public async Task TypeAsync_ValuePatternAvailable_UsesValuePatternStrategy()
    {
        var keys          = new MockKeySendInputProvider();
        var valueProvider = new FakeValuePatternProvider(hasValuePattern: true);
        var engine        = BuildEngine(keys, valueProvider);

        var options = new TypeOptions(Target: FakeElementRef.Create(), Text: "hello");
        var result  = await engine.TypeAsync(options);

        result.StrategyUsed.Should().Be(TypeStrategy.ValuePattern);
        result.Succeeded.Should().BeTrue();
        valueProvider.SetValueCalled.Should().BeTrue();
        // No keystroke events expected for this path.
        keys.Events.Should().BeEmpty();
    }

    // ── TypeAsync — Keystroke fallback ────────────────────────────────────────

    [Fact]
    public async Task TypeAsync_ValuePatternAbsent_UsesKeystrokeStrategy()
    {
        var keys          = new MockKeySendInputProvider();
        var valueProvider = new FakeValuePatternProvider(hasValuePattern: false);
        var engine        = BuildEngine(keys, valueProvider);

        var options = new TypeOptions(Target: FakeElementRef.Create(), Text: "hi");
        var result  = await engine.TypeAsync(options);

        result.StrategyUsed.Should().Be(TypeStrategy.Keystroke);
        result.Succeeded.Should().BeTrue();
        // Expect CharDown + CharUp per character.
        keys.Events.Should().HaveCount(4); // 'h' down, 'h' up, 'i' down, 'i' up
        keys.Events.Where(e => e.Kind == KeyEventKind.CharDown).Should().HaveCount(2);
        keys.Events.Where(e => e.Kind == KeyEventKind.CharUp).Should().HaveCount(2);
    }

    // ── SecretBuffer ──────────────────────────────────────────────────────────

    [Fact]
    public void SecretBuffer_ToString_ReturnsRedacted()
    {
        using var buf = new SecretBuffer(16);
        buf.ToString().Should().Be("***REDACTED***");
        buf.ToString().Should().NotContain("secret").And.NotContain("password");
    }

    [Fact]
    public void SecretBuffer_Span_AllowsWriteAndRead()
    {
        using var buf = new SecretBuffer(5);
        "hello".AsSpan().CopyTo(buf.Span);
        buf.Span[0].Should().Be('h');
        buf.Span[4].Should().Be('o');
    }

    // ── PressAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PressAsync_CtrlS_RecordsCorrectOrder()
    {
        var keys   = new MockKeySendInputProvider();
        var engine = BuildEngine(keys, new FakeValuePatternProvider(false));

        await engine.PressAsync("Ctrl+S");

        // Expected: Ctrl down, S down, S up, Ctrl up
        keys.Events.Should().HaveCount(4);
        keys.Events[0].Should().Be(new KeyEvent(KeyEventKind.KeyDown, "Ctrl"));
        keys.Events[1].Should().Be(new KeyEvent(KeyEventKind.KeyDown, "S"));
        keys.Events[2].Should().Be(new KeyEvent(KeyEventKind.KeyUp,   "S"));
        keys.Events[3].Should().Be(new KeyEvent(KeyEventKind.KeyUp,   "Ctrl"));
    }
}

// ── Test doubles ─────────────────────────────────────────────────────────────

/// <summary>
/// Stub <see cref="IValuePatternProvider"/> that reports configurable ValuePattern
/// availability and records whether SetValue was called.
/// </summary>
internal sealed class FakeValuePatternProvider : IValuePatternProvider
{
    private readonly bool _hasValuePattern;
    public bool SetValueCalled { get; private set; }

    public FakeValuePatternProvider(bool hasValuePattern) =>
        _hasValuePattern = hasValuePattern;

    public bool HasValuePattern(Tracto.Desktop.Element.ElementRef target) => _hasValuePattern;

    public void SetValue(Tracto.Desktop.Element.ElementRef target, string value) =>
        SetValueCalled = true;

    /// <summary>Always returns true so focus never blocks TypeAsync in these tests.</summary>
    public bool EnsureKeyboardFocus(Tracto.Desktop.Element.ElementRef target, int timeoutMs = 2000) => true;
}

/// <summary>
/// Provides a disposable-safe <see cref="Tracto.Desktop.Element.ElementRef"/> whose underlying
/// AutomationElement is the desktop root. Created lazily per-call to avoid static-init COM issues.
/// Falls back to an uninitialized placeholder when UIA is unavailable (e.g. headless CI),
/// which is safe for tests whose fakes never dereference the element.
/// </summary>
internal static class FakeElementRef
{
    private static readonly Tracto.Desktop.Element.ElementSelector Selector =
        new() { AutomationId = "fake" };

    /// <summary>Creates a fresh ElementRef backed by the UIA desktop root element.</summary>
    public static Tracto.Desktop.Element.ElementRef Create()
    {
        System.Windows.Automation.AutomationElement element;
        try
        {
            element = System.Windows.Automation.AutomationElement.RootElement;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // In headless environments the UIA desktop root is unavailable.
            // Create an uninitialized placeholder — sufficient for tests whose
            // fakes never dereference the underlying AutomationElement.
            element = (System.Windows.Automation.AutomationElement)
                System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                    typeof(System.Windows.Automation.AutomationElement));
        }
        return new(element, Selector);
    }
}
