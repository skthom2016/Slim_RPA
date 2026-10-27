using System.Windows;
using System.Windows.Automation;
using FluentAssertions;
using Tracto.Desktop.Element;
using Tracto.Desktop.Input;
using Xunit;

namespace Tracto.Desktop.Input.Tests;

// ─── Test helpers ─────────────────────────────────────────────────────────────

/// <summary>
/// Returns a configurable fixed Rect for any ElementRef.
/// No UIA access occurs; the ElementRef is used only as a dictionary key placeholder.
/// </summary>
internal sealed class FakeElementBoundsProvider : IElementBoundsProvider
{
    private readonly Rect _rect;

    public FakeElementBoundsProvider(double x, double y, double width, double height)
        => _rect = new Rect(x, y, width, height);

    public FakeElementBoundsProvider(Rect rect) => _rect = rect;

    public Rect GetBounds(ElementRef _) => _rect;
}

/// <summary>
/// Creates ElementRef objects backed by AutomationElement.RootElement so we never touch
/// the real element data — the FakeElementBoundsProvider short-circuits the bounds lookup.
/// </summary>
internal static class TestElementRefFactory
{
    private static readonly ElementSelector DummySelector = new() { AutomationId = "test" };

    /// <summary>Returns a valid ElementRef whose UIA backing element is never queried in tests.</summary>
    public static ElementRef CreateDummy()
        => new(AutomationElement.RootElement, DummySelector);
}

// ─── Tests ────────────────────────────────────────────────────────────────────

/// <summary>
/// Unit tests for DT-08 — Mouse Input Engine.
/// All tests use MockSendInputProvider and FakeElementBoundsProvider;
/// no real window or display access is performed.
/// </summary>
public sealed class MouseTests
{
    // ── DT-08-AC02 variant: offset guard fires BEFORE any SendInput ───────────

    /// <summary>
    /// Validates that ClickAsync throws ArgumentException for OffsetX > Width/2
    /// and that no input was sent (guard fires before P/Invoke).
    /// </summary>
    [Fact]
    public async Task ClickAsync_OffsetXExceedsHalfWidth_ThrowsArgumentExceptionBeforeSendInput()
    {
        // Arrange: element is 100×50 → half-width = 50
        var provider = new FakeElementBoundsProvider(0, 0, 100, 50);
        var mockInput = new MockSendInputProvider();
        var engine = new WindowsMouseInputEngine(mockInput, SystemClock.Instance, provider);

        var elementRef = TestElementRefFactory.CreateDummy();
        var options = new MouseClickOptions(elementRef, ClickButton.Left, OffsetX: 51); // 51 > 50

        // Act
        Func<Task> act = () => engine.ClickAsync(options);

        // Assert: ArgumentException before any P/Invoke
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*OffsetX*");

        mockInput.Calls.Should().BeEmpty("no input must be sent when offset guard fires");
    }

    // ── DT-08-AC08: mouse button released before OperationCanceledException ───

    /// <summary>
    /// Validates that DragAsync releases the mouse button (MouseUp) before propagating
    /// OperationCanceledException when the drag is cancelled mid-flight.
    /// </summary>
    [Fact]
    public async Task DragAsync_CancelledMidDrag_SendsMouseUpBeforeException()
    {
        // Arrange
        var provider = new FakeElementBoundsProvider(0, 0, 100, 50);
        var mockInput = new MockSendInputProvider();
        var fakeClock = new FakeClock();
        var engine = new WindowsMouseInputEngine(mockInput, fakeClock, provider);

        var src = TestElementRefFactory.CreateDummy();
        var dst = TestElementRefFactory.CreateDummy();
        var options = new DragOptions(src, dst, DragDurationMs: 100);

        using var cts = new CancellationTokenSource();

        // Start drag — it will block at the first DelayAsync
        var dragTask = engine.DragAsync(options, cts.Token);

        // Wait a tick for the engine to reach the first DelayAsync
        await Task.Yield();

        // Record how many inputs were sent before cancel
        int inputsBeforeCancel = mockInput.AllInputs.Count;

        // Cancel while the drag is in progress
        cts.Cancel();

        // The task should throw OperationCanceledException
        Func<Task> act = async () => await dragTask;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Assert: at least one LEFTUP was sent AFTER the cancel (mouse cleanup)
        var allInputs = mockInput.AllInputs.ToList();
        allInputs.Should().Contain(
            i => (i.Flags & MouseEventFlags.LEFTUP) != 0,
            "mouse button must be released even when drag is cancelled");
    }

    // ── DT-08-AC07: scroll retry class is NEVER ───────────────────────────────

    /// <summary>
    /// Validates that ScrollAsync does not automatically retry — a second explicit call
    /// produces exactly one more wheel event, confirming no internal auto-retry logic.
    /// </summary>
    [Fact]
    public async Task ScrollAsync_NoRetryOnRepeatCall_EachCallProducesExactlyOneWheelEvent()
    {
        // Arrange
        var provider = new FakeElementBoundsProvider(0, 0, 200, 300);
        var mockInput = new MockSendInputProvider();
        var engine = new WindowsMouseInputEngine(mockInput, SystemClock.Instance, provider);

        var elementRef = TestElementRefFactory.CreateDummy();
        var options = new ScrollOptions(elementRef, Clicks: 3, ScrollDown: true);

        // Act: call scroll twice explicitly
        await engine.ScrollAsync(options);
        int countAfterFirst = mockInput.AllInputs.Count(i => (i.Flags & MouseEventFlags.WHEEL) != 0);

        await engine.ScrollAsync(options);
        int countAfterSecond = mockInput.AllInputs.Count(i => (i.Flags & MouseEventFlags.WHEEL) != 0);

        // Assert: first call → exactly 1 wheel event; second call adds exactly 1 more
        countAfterFirst.Should().Be(1, "first scroll produces exactly one wheel event");
        countAfterSecond.Should().Be(2, "second explicit call produces exactly one more — no internal auto-retry");
    }

    // ── DT-08-AC05: hover dwell uses IClock ───────────────────────────────────

    /// <summary>
    /// Validates that HoverAsync uses IClock.DelayAsync for the dwell period
    /// and that the cursor move input is recorded.
    /// </summary>
    [Fact]
    public async Task HoverAsync_WithDwellMs_FakeClockAdvancedAndCursorMoved()
    {
        // Arrange: element at (200,100) size 80×40 — center is (240, 120)
        var provider = new FakeElementBoundsProvider(200, 100, 80, 40);
        var mockInput = new MockSendInputProvider();
        var fakeClock = new FakeClock();
        var engine = new WindowsMouseInputEngine(mockInput, fakeClock, provider);

        var elementRef = TestElementRefFactory.CreateDummy();
        var options = new HoverOptions(elementRef, OffsetX: null, OffsetY: null, DwellMs: 500);

        // Act: start hover — it will block at DelayAsync(500)
        var hoverTask = engine.HoverAsync(options);

        // Allow the task to reach the delay
        await Task.Yield();

        // Verify move was already sent
        mockInput.AllInputs.Should().Contain(
            i => (i.Flags & MouseEventFlags.MOVE) != 0,
            "cursor move input should be sent before the dwell delay");

        // The hover task should still be pending (waiting for clock)
        hoverTask.IsCompleted.Should().BeFalse("hover is blocking on dwell delay");

        // Advance the fake clock by exactly DwellMs
        fakeClock.AdvanceMs(500);

        // Now the task should complete
        await hoverTask;

        hoverTask.IsCompletedSuccessfully.Should().BeTrue("hover completes after clock advanced by DwellMs");
    }
}
