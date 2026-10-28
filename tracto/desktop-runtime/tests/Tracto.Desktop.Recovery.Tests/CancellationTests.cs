using FluentAssertions;
using Tracto.Desktop.Input;
using Tracto.Desktop.Recovery;
using Tracto.Desktop.Session;
using Xunit;

namespace Tracto.Desktop.Recovery.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Mock helpers
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Records all KeyUp calls so tests can assert modifier-key release count/names.
/// </summary>
file sealed class MockSendInputProvider : IKeySendInputProvider, ISendInputProvider
{
    // ── IKeySendInputProvider ─────────────────────────────────────────────────
    public List<string> KeyUpCalls { get; } = new();
    public List<string> KeyDownCalls { get; } = new();

    public void KeyDown(string key) => KeyDownCalls.Add(key);
    public void KeyUp(string key)   => KeyUpCalls.Add(key);
    public void CharDown(char ch)   { }
    public void CharUp(char ch)     { }
    public void SetClipboardText(string text) { }
    public void SendPaste()         { }

    // ── ISendInputProvider ────────────────────────────────────────────────────
    public List<MouseInput[]> MouseInputCalls { get; } = new();
    public void SendMouseInput(MouseInput[] inputs) => MouseInputCalls.Add(inputs);
}

/// <summary>
/// Records ForceCloseHandle invocations.
/// </summary>
file sealed class MockClipboardEngine : IClipboardEngine
{
    public int ForceCloseHandleCallCount { get; private set; }

    public void ForceCloseHandle() => ForceCloseHandleCallCount++;

    // The other members are not exercised by DT-17 tests.
    public Task<ClipboardRef> CopyAsync(
        Tracto.Desktop.Element.ElementRef target,
        string refName,
        CancellationToken ct = default)
        => throw new NotImplementedException();

    public Task PasteAsync(
        ClearableBuffer content,
        Tracto.Desktop.Element.ElementRef target,
        ClipboardPastePolicy policy,
        CancellationToken ct = default)
        => throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// Test class
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Unit tests for DT-17: safe cancellation and resource release.
/// </summary>
public sealed class CancellationTests
{
    // =========================================================================
    // DT-17-AC01 — HeldModifierKeys → ModifierKeyReleaseStep sends key-up events
    // =========================================================================

    [Fact]
    public async Task DT17_AC01_HeldModifierKeys_TwoKeyUpEventsSent()
    {
        // Arrange
        var mock = new MockSendInputProvider();
        var step = new ModifierKeyReleaseStep(mock);
        var ctx  = new CancellationContext(
            JobId:              "job-001",
            AttemptId:          "attempt-001",
            HeldModifierKeys:   new[] { "Ctrl", "Shift" },
            IsMouseButtonDown:  false,
            ClipboardHandleOpen: false);

        // Act
        await step.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        mock.KeyUpCalls.Should().HaveCount(2, "one KeyUp per held modifier key");
        mock.KeyUpCalls.Should().Contain("Ctrl");
        mock.KeyUpCalls.Should().Contain("Shift");
    }

    [Fact]
    public async Task DT17_AC01_NoHeldModifierKeys_NoKeyUpEvents()
    {
        // Arrange
        var mock = new MockSendInputProvider();
        var step = new ModifierKeyReleaseStep(mock);
        var ctx  = new CancellationContext(
            JobId:              "job-002",
            AttemptId:          "attempt-002",
            HeldModifierKeys:   Array.Empty<string>(),
            IsMouseButtonDown:  false,
            ClipboardHandleOpen: false);

        // Act
        await step.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        mock.KeyUpCalls.Should().BeEmpty();
    }

    // =========================================================================
    // DT-17-AC02 — IsMouseButtonDown → MouseButtonReleaseStep sends mouse-up event
    // =========================================================================

    [Fact]
    public async Task DT17_AC02_MouseButtonDown_MouseUpEventSent()
    {
        // Arrange
        var mock = new MockSendInputProvider();
        var step = new MouseButtonReleaseStep(mock);
        var ctx  = new CancellationContext(
            JobId:              "job-003",
            AttemptId:          "attempt-003",
            HeldModifierKeys:   Array.Empty<string>(),
            IsMouseButtonDown:  true,
            ClipboardHandleOpen: false);

        // Act
        await step.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        mock.MouseInputCalls.Should().HaveCount(1, "one SendMouseInput call expected");
        mock.MouseInputCalls[0].Should().ContainSingle();
        mock.MouseInputCalls[0][0].Flags.Should().Be(MouseEventFlags.LEFTUP,
            "MOUSEEVENTF_LEFTUP must be set");
    }

    [Fact]
    public async Task DT17_AC02_MouseButtonNotDown_NoMouseEventSent()
    {
        // Arrange
        var mock = new MockSendInputProvider();
        var step = new MouseButtonReleaseStep(mock);
        var ctx  = new CancellationContext(
            JobId:              "job-004",
            AttemptId:          "attempt-004",
            HeldModifierKeys:   Array.Empty<string>(),
            IsMouseButtonDown:  false,
            ClipboardHandleOpen: false);

        // Act
        await step.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        mock.MouseInputCalls.Should().BeEmpty();
    }

    // =========================================================================
    // DT-17-AC03 — ClipboardHandleOpen → ClipboardHandleReleaseStep calls ForceCloseHandle
    // =========================================================================

    [Fact]
    public async Task DT17_AC03_ClipboardOpen_ForceCloseHandleCalled()
    {
        // Arrange
        var engine = new MockClipboardEngine();
        var step   = new ClipboardHandleReleaseStep(engine);
        var ctx    = new CancellationContext(
            JobId:              "job-005",
            AttemptId:          "attempt-005",
            HeldModifierKeys:   Array.Empty<string>(),
            IsMouseButtonDown:  false,
            ClipboardHandleOpen: true);

        // Act
        await step.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        engine.ForceCloseHandleCallCount.Should().Be(1,
            "ForceCloseHandle must be called exactly once when handle is open");
    }

    [Fact]
    public async Task DT17_AC03_ClipboardNotOpen_ForceCloseHandleNotCalled()
    {
        // Arrange
        var engine = new MockClipboardEngine();
        var step   = new ClipboardHandleReleaseStep(engine);
        var ctx    = new CancellationContext(
            JobId:              "job-006",
            AttemptId:          "attempt-006",
            HeldModifierKeys:   Array.Empty<string>(),
            IsMouseButtonDown:  false,
            ClipboardHandleOpen: false);

        // Act
        await step.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        engine.ForceCloseHandleCallCount.Should().Be(0);
    }

    // =========================================================================
    // DT-17-AC04 — InputLeaseReleaseStep → InputLeaseManager.IsLeaseHeld = false
    // =========================================================================

    [Fact]
    public async Task DT17_AC04_InputLeaseReleased_IsLeaseHeldFalse()
    {
        // Arrange
        var leaseManager = new InputLeaseManager();
        bool acquired = leaseManager.TryAcquireLease("job-007");
        acquired.Should().BeTrue("lease should be acquired before the test");

        var step = new InputLeaseReleaseStep(leaseManager);
        var ctx  = new CancellationContext(
            JobId:              "job-007",
            AttemptId:          "attempt-007",
            HeldModifierKeys:   Array.Empty<string>(),
            IsMouseButtonDown:  false,
            ClipboardHandleOpen: false);

        // Act
        await step.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        leaseManager.IsLeaseHeld.Should().BeFalse(
            "the release step must return the lease to the manager");
    }

    // =========================================================================
    // DT-17-AC05 — Step 2 throws → Step 3 still runs; AggregateException thrown
    // =========================================================================

    [Fact]
    public async Task DT17_AC05_MiddleStepThrows_SubsequentStepsStillRun_AggregateExceptionThrown()
    {
        // Arrange
        var executionOrder = new List<int>();

        var step1 = new LambdaStep(async (_, _) => { executionOrder.Add(1); await Task.CompletedTask; });
        var step2 = new LambdaStep((_, _) => throw new InvalidOperationException("step 2 failed"));
        var step3 = new LambdaStep(async (_, _) => { executionOrder.Add(3); await Task.CompletedTask; });

        var sequence = new CancellationReleaseSequence();
        var ctx = new CancellationContext(
            JobId:              "job-008",
            AttemptId:          "attempt-008",
            HeldModifierKeys:   Array.Empty<string>(),
            IsMouseButtonDown:  false,
            ClipboardHandleOpen: false);

        // Act
        Func<Task> act = () => sequence.RunAsync(ctx, new IResourceReleaseStep[] { step1, step2, step3 },
            CancellationToken.None);

        // Assert
        var aggEx = await act.Should().ThrowAsync<AggregateException>(
            "AggregateException must be thrown when any step fails");
        aggEx.Which.InnerExceptions.Should().HaveCount(1);
        aggEx.Which.InnerExceptions[0].Message.Should().Be("step 2 failed");

        executionOrder.Should().ContainInOrder(1, 3)
            .And.HaveCount(2, "step 3 must execute even though step 2 threw");
    }

    [Fact]
    public async Task DT17_AllStepsSucceed_NoExceptionThrown()
    {
        // Arrange
        var executionOrder = new List<int>();
        var step1 = new LambdaStep(async (_, _) => { executionOrder.Add(1); await Task.CompletedTask; });
        var step2 = new LambdaStep(async (_, _) => { executionOrder.Add(2); await Task.CompletedTask; });

        var sequence = new CancellationReleaseSequence();
        var ctx = new CancellationContext(
            JobId:              "job-009",
            AttemptId:          "attempt-009",
            HeldModifierKeys:   Array.Empty<string>(),
            IsMouseButtonDown:  false,
            ClipboardHandleOpen: false);

        // Act
        Func<Task> act = () => sequence.RunAsync(ctx, new IResourceReleaseStep[] { step1, step2 },
            CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
        executionOrder.Should().ContainInOrder(1, 2);
    }

    // =========================================================================
    // CancellationResultClassifier — classify no-input vs input-sent
    // =========================================================================

    [Fact]
    public void CancellationResultClassifier_NoInputSent_ReturnsCancelled()
    {
        var outcome = CancellationResultClassifier.Classify(anyInputSent: false);
        outcome.Should().Be(CancellationOutcome.Cancelled);
    }

    [Fact]
    public void CancellationResultClassifier_InputSent_ReturnsUnknown()
    {
        var outcome = CancellationResultClassifier.Classify(anyInputSent: true);
        outcome.Should().Be(CancellationOutcome.Unknown);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────────────────────

file sealed class LambdaStep : IResourceReleaseStep
{
    private readonly Func<CancellationContext, CancellationToken, Task> _fn;
    public LambdaStep(Func<CancellationContext, CancellationToken, Task> fn) => _fn = fn;
    public Task ExecuteAsync(CancellationContext ctx, CancellationToken ct) => _fn(ctx, ct);
}
