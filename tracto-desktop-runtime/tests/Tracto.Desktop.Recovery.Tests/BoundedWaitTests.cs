using System.Windows.Automation;
using FluentAssertions;
using Tracto.Desktop.Element;
using Tracto.Desktop.Recovery;
using Xunit;

namespace Tracto.Desktop.Recovery.Tests;

/// <summary>
/// Unit tests for DT-15: bounded waits, FakeTimer, and PostconditionAssertion.
/// FakeTimer is advanced with a single AdvanceMs call per test to avoid
/// scheduling races between async continuations and subsequent advances.
/// </summary>
public sealed class BoundedWaitTests
{
    // =========================================================================
    // DT-15-AC01 — FakeTimer advanced to 1000ms; element appears at T=1000 → Resolved=true
    // =========================================================================
    [Fact]
    public async Task WaitAsync_ElementAppearsWithinTimeout_ResolvesTrue()
    {
        // Arrange: finder throws TARGET_NOT_FOUND on the first call, returns element on the second.
        var timer = new FakeTimer();
        var finder = new SequencedMockFinder(returnOnSecondCall: MakeElementRef(new ElementSelector { AutomationId = "btn" }));
        var checker = new AlwaysOkChecker();
        var waiter = new ElementWaiter(finder, checker, timer);

        var opts = new WaitOptions
        {
            Condition = WaitCondition.Exists,
            TimeoutMs = 5000,
            PollIntervalMs = 1000,
        };

        // Act: start the wait, advance the timer once to 1000ms, then await completion.
        var task = waiter.WaitAsync(new ElementSelector { AutomationId = "btn" }, opts, CancellationToken.None);

        // First check already ran synchronously (finder call 1 → not found, starts delay of 1000ms).
        // Advance timer to 1000ms: the first delay completes; WaitAsync's continuation will run
        // asynchronously (thread pool) and execute the second check (finder call 2 → found).
        timer.AdvanceMs(1000);

        var result = await task; // waits for the thread-pool continuation to finish

        // Assert
        result.Resolved.Should().BeTrue();
        result.ElapsedMs.Should().BeGreaterThanOrEqualTo(0);
    }

    // =========================================================================
    // DT-15-AC02 — FakeTimer advanced past timeout; element never appears → DesktopActionError(TIMEOUT)
    //
    // Design note: PollIntervalMs > TimeoutMs so a single advance (1000ms) pushes elapsed
    // past the 500ms timeout in ONE iteration without requiring a second advance before
    // the async continuation has re-registered a new delay in FakeTimer.
    // =========================================================================
    [Fact]
    public async Task WaitAsync_ElementNeverAppears_ThrowsTimeout()
    {
        // Arrange: finder always throws TARGET_NOT_FOUND.
        var timer = new FakeTimer();
        var finder = new AlwaysNotFoundFinder();
        var checker = new AlwaysOkChecker();
        var waiter = new ElementWaiter(finder, checker, timer);

        var opts = new WaitOptions
        {
            Condition = WaitCondition.Visible,
            TimeoutMs = 500,
            PollIntervalMs = 1000, // single advance of 1000ms exceeds the 500ms timeout
        };

        var task = waiter.WaitAsync(new ElementSelector { AutomationId = "missingBtn" }, opts, CancellationToken.None);

        // One advance pushes elapsed from 0 → 1000ms, which exceeds TimeoutMs=500.
        // The next WaitAsync iteration sees elapsed(1000) >= timeout(500) and throws TIMEOUT.
        timer.AdvanceMs(1000);

        // Assert
        var ex = await Assert.ThrowsAsync<DesktopActionError>(() => task);

        ex.ErrorCategory.Should().Be("TIMEOUT");
        ex.Message.Should().Contain("not_found"); // last observed state in message
    }

    // =========================================================================
    // DT-15 — PostconditionAssertion predicate=false → DesktopActionError(ENVIRONMENT)
    // =========================================================================
    [Fact]
    public async Task PostconditionAssertion_PredicateReturnsFalse_ThrowsEnvironmentError()
    {
        // Arrange: checker returns a fully-ok state; predicate always returns false.
        var checker = new AlwaysOkChecker();
        var assertion = new PostconditionAssertion(checker);

        var el = MakeElementRef(new ElementSelector { AutomationId = "btn" });
        Func<ElementStateResult, bool> alwaysFalse = _ => false;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DesktopActionError>(
            () => assertion.AssertAsync(el, alwaysFalse, CancellationToken.None));

        ex.ErrorCategory.Should().Be("ENVIRONMENT");
        ex.DetailCode.Should().Be("desktop.postconditionFailed");
    }

    // =========================================================================
    // DT-15 — PostconditionAssertion succeeds when predicate returns true
    // =========================================================================
    [Fact]
    public async Task PostconditionAssertion_PredicateReturnsTrue_DoesNotThrow()
    {
        var checker = new AlwaysOkChecker();
        var assertion = new PostconditionAssertion(checker);
        var el = MakeElementRef(new ElementSelector { AutomationId = "btn" });

        Func<Task> act = () => assertion.AssertAsync(el, _ => true, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // =========================================================================
    // DT-15 — FakeTimer: DelayAsync with ms=0 completes immediately
    // =========================================================================
    [Fact]
    public async Task FakeTimer_ZeroDelay_CompletesImmediately()
    {
        var timer = new FakeTimer();
        await timer.DelayAsync(0, CancellationToken.None);
        // Should return Task.CompletedTask without any TCS involvement.
    }

    // =========================================================================
    // DT-15 — FakeTimer: AdvanceMs past multiple pending delays completes all
    // =========================================================================
    [Fact]
    public void FakeTimer_AdvancePastMultiplePending_CompletesAll()
    {
        var timer = new FakeTimer();

        var t1 = timer.DelayAsync(100, CancellationToken.None);
        var t2 = timer.DelayAsync(200, CancellationToken.None);
        var t3 = timer.DelayAsync(300, CancellationToken.None);

        timer.AdvanceMs(350); // advances past all three

        t1.IsCompletedSuccessfully.Should().BeTrue();
        t2.IsCompletedSuccessfully.Should().BeTrue();
        t3.IsCompletedSuccessfully.Should().BeTrue();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ElementRef MakeElementRef(ElementSelector sel)
        => new ElementRef(AutomationElement.RootElement, sel);

    // -------------------------------------------------------------------------
    // Finder: throws TARGET_NOT_FOUND on call 1, returns element on call 2+.
    // -------------------------------------------------------------------------
    private sealed class SequencedMockFinder : IElementFinder
    {
        private readonly ElementRef _elementOnSuccess;
        private int _callCount;

        public SequencedMockFinder(ElementRef returnOnSecondCall)
        {
            _elementOnSuccess = returnOnSecondCall;
        }

        public Task<ElementRef> FindAsync(ElementSelector sel, ElementFindOptions opts, CancellationToken ct)
        {
            _callCount++;
            if (_callCount == 1)
                throw new DesktopActionError("TARGET_NOT_FOUND", "desktop.elementNotFound");
            return Task.FromResult(_elementOnSuccess);
        }

        public Task<ElementRef> ReResolveAsync(ElementRef stale, ElementFindOptions opts, CancellationToken ct)
            => FindAsync(stale.Selector, opts, ct);
    }

    // -------------------------------------------------------------------------
    // Finder that always throws TARGET_NOT_FOUND.
    // -------------------------------------------------------------------------
    private sealed class AlwaysNotFoundFinder : IElementFinder
    {
        public Task<ElementRef> FindAsync(ElementSelector sel, ElementFindOptions opts, CancellationToken ct)
            => throw new DesktopActionError("TARGET_NOT_FOUND", "desktop.elementNotFound");

        public Task<ElementRef> ReResolveAsync(ElementRef stale, ElementFindOptions opts, CancellationToken ct)
            => throw new DesktopActionError("TARGET_NOT_FOUND", "desktop.elementNotFound");
    }

    // -------------------------------------------------------------------------
    // Checker that always returns a fully-ok state (ignores ElementRef).
    // -------------------------------------------------------------------------
    private sealed class AlwaysOkChecker : IElementStateChecker
    {
        public Task<ElementStateResult> CheckAsync(ElementRef el, CancellationToken ct)
            => Task.FromResult(new ElementStateResult
            {
                IsEnabled = true,
                IsVisible = true,
                HasProvider = true,
                AccessDeniedReason = null,
            });
    }
}
