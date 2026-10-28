using FluentAssertions;
using Tracto.Desktop.Element;
using Tracto.Desktop.Input;
using Xunit;

namespace Tracto.Desktop.Input.Tests;

/// <summary>
/// Unit tests for DT-10 clipboard copy/paste with contention recovery and restore pattern.
/// All tests use <see cref="MockClipboardApiProvider"/> and <see cref="NoOpKeyInputSimulator"/>
/// so no real Windows UIA tree or OS clipboard is required.
/// </summary>
public sealed class ClipboardTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an engine wired to the supplied mock provider.
    /// delayMs is 0 so contention tests do not sleep.
    /// </summary>
    private static WindowsClipboardEngine BuildEngine(
        MockClipboardApiProvider mock,
        int maxAttempts = 10,
        int delayMs = 0)
    {
        var policy = new ContentionRetryPolicy(mock, maxAttempts, delayMs);
        return new WindowsClipboardEngine(mock, policy, new NoOpKeyInputSimulator());
    }

    /// <summary>
    /// Creates a <see cref="ClearableBuffer"/> pre-filled with the given text.
    /// ClearableBuffer(int capacity) is the only public ctor; we copy chars in.
    /// </summary>
    private static ClearableBuffer MakeBuffer(string text)
    {
        var buf = new ClearableBuffer(text.Length == 0 ? 1 : text.Length);
        text.AsSpan().CopyTo(buf.Span);
        return buf;
    }

    // Null ElementRef is safe for these tests because NoOpKeyInputSimulator ignores it.
    private static ElementRef NullElement =>
        null!;  // NoOp simulator never dereferences it.

    // ── DT-10-AC03 — Contention recovery ────────────────────────────────────

    /// <summary>
    /// MockClipboardApiProvider.TryOpen fails 3 times then succeeds.
    /// CopyAsync must succeed and must not throw on the first failure.
    /// </summary>
    [Fact]
    public async Task CopyAsync_ContentionFailsThenSucceeds_ReturnsClipboardRef()
    {
        var mock = new MockClipboardApiProvider();
        mock.ClipboardText = "hello world";
        mock.SimulateContentionFor(3);   // first 3 calls return false

        var engine = BuildEngine(mock, maxAttempts: 10);

        ClipboardRef result = await engine.CopyAsync(NullElement, "myRef");

        result.RefName.Should().Be("myRef");
        // Exactly 4 open calls: 3 failures + 1 success.
        mock.OpenAttempts.Should().Be(4);
        // CloseClipboard must be called once (in the finally block).
        mock.CloseCallCount.Should().BeGreaterThanOrEqualTo(1);
    }

    /// <summary>
    /// First TryOpen call fails but subsequent ones succeed — engine must not throw
    /// on the very first failure; it must continue retrying.
    /// </summary>
    [Fact]
    public async Task CopyAsync_FirstAttemptFails_DoesNotThrowOnFirstFailure()
    {
        var mock = new MockClipboardApiProvider();
        mock.ClipboardText = "data";
        mock.SimulateContentionFor(1);   // only first call fails

        var engine = BuildEngine(mock, maxAttempts: 5);

        Func<Task> act = () => engine.CopyAsync(NullElement, "ref1");
        await act.Should().NotThrowAsync();
        mock.OpenAttempts.Should().Be(2);  // 1 failure + 1 success
    }

    // ── DT-10-AC04 — BestEffortRestore ──────────────────────────────────────

    /// <summary>
    /// With BestEffortRestore policy:
    ///   1. Snapshot is captured from the mock clipboard before the paste.
    ///   2. After paste the mock clipboard is restored to the snapshot value.
    /// </summary>
    [Fact]
    public async Task PasteAsync_BestEffortRestore_SnapshotCapturedAndRestoredAfterPaste()
    {
        const string originalClipboard = "original clipboard content";
        const string pasteText         = "new paste value";

        var mock = new MockClipboardApiProvider();
        mock.ClipboardText = originalClipboard;   // pre-paste clipboard state

        var engine = BuildEngine(mock);

        using var content = MakeBuffer(pasteText);

        // PasteAsync always throws UnknownOutcomeException (uncertain paste).
        Func<Task> act = () =>
            engine.PasteAsync(content, NullElement, ClipboardPastePolicy.BestEffortRestore);

        await act.Should().ThrowAsync<UnknownOutcomeException>();

        // The mock clipboard should be back to the original value after restore.
        mock.ClipboardText.Should().Be(originalClipboard);
    }

    /// <summary>
    /// With NoRestore policy the clipboard is NOT restored: it holds the paste text.
    /// </summary>
    [Fact]
    public async Task PasteAsync_NoRestore_ClipboardNotRestored()
    {
        const string originalClipboard = "original";
        const string pasteText         = "pasted";

        var mock = new MockClipboardApiProvider();
        mock.ClipboardText = originalClipboard;

        var engine = BuildEngine(mock);

        using var content = MakeBuffer(pasteText);

        Func<Task> act = () =>
            engine.PasteAsync(content, NullElement, ClipboardPastePolicy.NoRestore);

        await act.Should().ThrowAsync<UnknownOutcomeException>();

        // No restore — clipboard should hold the paste text (last SetText call).
        mock.ClipboardText.Should().Be(pasteText);
    }

    // ── DT-10-AC02 — Retry class NEVER ──────────────────────────────────────

    /// <summary>
    /// PasteAsync retry class is NEVER: a second call is a fully separate invocation.
    /// Calling PasteAsync twice should result in two independent executions,
    /// each throwing UnknownOutcomeException — no internal retry between them.
    /// </summary>
    [Fact]
    public async Task PasteAsync_RetryClassNever_SecondCallIsIndependentInvocation()
    {
        var mock = new MockClipboardApiProvider();
        mock.ClipboardText = string.Empty;

        var engine = BuildEngine(mock);

        using var content1 = MakeBuffer("first");
        using var content2 = MakeBuffer("second");

        // First call.
        int openCountAfterFirst;
        await Assert.ThrowsAsync<UnknownOutcomeException>(
            () => engine.PasteAsync(content1, NullElement, ClipboardPastePolicy.NoRestore));
        openCountAfterFirst = mock.OpenAttempts;
        openCountAfterFirst.Should().BeGreaterThan(0);

        // Second call — fully independent.
        await Assert.ThrowsAsync<UnknownOutcomeException>(
            () => engine.PasteAsync(content2, NullElement, ClipboardPastePolicy.NoRestore));

        // Open count should have increased by the engine's single paste-open per call.
        mock.OpenAttempts.Should().BeGreaterThan(openCountAfterFirst);
    }

    // ── DT-17-AC03 — ForceCloseHandle on exception ──────────────────────────

    /// <summary>
    /// When an exception is thrown after the clipboard is opened but before GetText,
    /// CloseClipboard must still be called (via finally).
    /// We simulate this by making GetText throw, then call ForceCloseHandle and verify
    /// the provider registered the close.
    /// </summary>
    [Fact]
    public async Task CopyAsync_ExceptionAfterOpenBeforeGetText_CloseCalledInFinally()
    {
        var mock = new ThrowOnGetTextClipboardProvider("simulated error");

        var policy = new ContentionRetryPolicy(mock, maxAttempts: 1, delayMs: 0);
        var engine = new WindowsClipboardEngine(mock, policy, new NoOpKeyInputSimulator());

        Func<Task> act = () => engine.CopyAsync(NullElement, "ref");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*simulated error*");

        // CloseClipboard must have been called from the finally block.
        mock.CloseCallCount.Should().BeGreaterThanOrEqualTo(1);

        // ForceCloseHandle is callable again without error (idempotent).
        engine.ForceCloseHandle();
        mock.CloseCallCount.Should().BeGreaterThanOrEqualTo(2);
    }
}

// ── Test helpers ─────────────────────────────────────────────────────────────

/// <summary>
/// Clipboard provider that opens successfully but throws from <see cref="GetText"/>
/// to simulate an error between Open and GetText.
/// </summary>
internal sealed class ThrowOnGetTextClipboardProvider : IClipboardApiProvider
{
    private readonly string _message;

    internal ThrowOnGetTextClipboardProvider(string message) => _message = message;

    public int CloseCallCount { get; private set; }

    public bool TryOpenClipboard(IntPtr owner) => true;

    public void CloseClipboard() => CloseCallCount++;

    public string? GetText() =>
        throw new InvalidOperationException(_message);

    public void SetText(string text) { }
}
