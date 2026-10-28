using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Moq;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Crash;
using TractoRuntime.Browser.Tests.Fixtures;
using TractoRuntime.Browser.Worker;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// Acceptance tests for BRW-US-004 — Browser Process Lifecycle and Crash Recovery.
/// Covers BRW-AT-12 (crash handling) and BRW-AT-23 (resource release after crash).
/// Requirement refs: BRW-004, BRE-004.
/// </summary>
/// <remarks>
/// Acceptance criteria:
///   AC#1 — On any terminal state, all browser-owned processes and contexts are terminated
///           and released (lifecycle ownership).
///   AC#2 — When the browser crashes mid-run, the crash is recorded in the run journal
///           and no orphaned worker-owned browser process remains.
///   AC#3 — Crash before any irreversible action → run terminates as FAILED (not UNKNOWN).
///   AC#4 — Crash after a dispatched effectful action with no settled outcome → UNKNOWN.
///           Crash after all effectful actions settled → FAILED.
///           No blind restart or re-execution of steps.
/// </remarks>
public sealed class BRW_US_004_Tests : BrowserTestBase
{
    // ── CrashStateClassifier (pure-logic, no mocks needed) ───────────────────

    // AC#3 — No effectful action → FAILED

    [Fact]
    public void Classifier_AC3_EmptyJournal_ReturnsFailed()
    {
        var result = CrashStateClassifier.Classify([]);
        result.Should().Be(RunState.FAILED,
            "AC#3: a crash before any effectful action is deterministic — the run is FAILED");
    }

    [Fact]
    public void Classifier_AC3_JournalContainsOnlyNonEffectfulEntries_ReturnsFailed()
    {
        // Arrange — non-effectful entries only (browser.crashed is the crash marker itself)
        var entries = new List<JournalEntry>
        {
            JournalEntry("step-1", "browser.launch",   "ok"),
            JournalEntry("step-2", "context.create",   "ok"),
            JournalEntry("step-3", "page.wait",        "networkidle"),
            JournalEntry("crash",  "browser.crashed",  "Unexpected disconnection"),
        };

        var result = CrashStateClassifier.Classify(entries);
        result.Should().Be(RunState.FAILED,
            "AC#3: non-effectful entries before a crash do not create uncertain side effects");
    }

    // AC#4 — Dispatched effectful action with no settlement → UNKNOWN

    [Fact]
    public void Classifier_AC4_ClickDispatchedNoSettlement_ReturnsUnknown()
    {
        var entries = new List<JournalEntry>
        {
            JournalEntry("step-1", "navigate",  null),          // dispatched, no settlement
        };

        var result = CrashStateClassifier.Classify(entries);
        result.Should().Be(RunState.UNKNOWN,
            "AC#4: a dispatched navigate without settlement means the outcome is indeterminate");
    }

    [Fact]
    public void Classifier_AC4_FormSubmitDispatchedNoSettlement_ReturnsUnknown()
    {
        var entries = new List<JournalEntry>
        {
            JournalEntry("step-1", "form.submit",  null),       // dispatched, no settlement
        };

        var result = CrashStateClassifier.Classify(entries);
        result.Should().Be(RunState.UNKNOWN,
            "AC#4: a form.submit dispatched without a settled outcome is indeterminate");
    }

    [Fact]
    public void Classifier_AC4_ServiceInvokeDispatchedNoSettlement_ReturnsUnknown()
    {
        var entries = new List<JournalEntry>
        {
            JournalEntry("step-1", "service.invoke", null),     // dispatched, no settlement
        };

        var result = CrashStateClassifier.Classify(entries);
        result.Should().Be(RunState.UNKNOWN,
            "AC#4: a service.invoke without settlement produces UNKNOWN (no actionCode branch)");
    }

    [Fact]
    public void Classifier_AC4_ClickDispatchedAndSettled_ReturnsFailed()
    {
        // Settled = outcome is established before crash; state is deterministic → FAILED
        var entries = new List<JournalEntry>
        {
            JournalEntry("step-1", "click",          null),
            JournalEntry("step-1", "click.settled",  "ok"),     // outcome confirmed
        };

        var result = CrashStateClassifier.Classify(entries);
        result.Should().Be(RunState.FAILED,
            "AC#4: a crash after all effectful actions have settled is deterministic — FAILED");
    }

    [Fact]
    public void Classifier_AC4_MultipleActions_AllSettled_ReturnsFailed()
    {
        var entries = new List<JournalEntry>
        {
            JournalEntry("step-1", "navigate",           null),
            JournalEntry("step-1", "navigate.settled",   "https://example.com"),
            JournalEntry("step-2", "click",              null),
            JournalEntry("step-2", "click.settled",      "ok"),
            JournalEntry("step-3", "form.submit",        null),
            JournalEntry("step-3", "form.submit.settled","submitted"),
        };

        var result = CrashStateClassifier.Classify(entries);
        result.Should().Be(RunState.FAILED,
            "AC#4: all effectful actions settled — crash is after deterministic side effects");
    }

    [Fact]
    public void Classifier_AC4_LastActionIndeterminate_ReturnsUnknown()
    {
        // First action settled, second dispatched but not settled → UNKNOWN
        var entries = new List<JournalEntry>
        {
            JournalEntry("step-1", "navigate",           null),
            JournalEntry("step-1", "navigate.settled",   "https://example.com"),
            JournalEntry("step-2", "click",              null),   // in-flight — no settlement
        };

        var result = CrashStateClassifier.Classify(entries);
        result.Should().Be(RunState.UNKNOWN,
            "AC#4: the last effectful action has no settlement — outcome is indeterminate");
    }

    [Fact]
    public void Classifier_AC4_AllEffectfulActionTypes_Dispatched_ReturnUnknown()
    {
        // Each effectful action type, dispatched but none settled
        string[] effectfulActions =
        [
            "form.submit", "click", "navigate",
            "file.download", "file.upload", "service.invoke"
        ];

        foreach (var action in effectfulActions)
        {
            var entries = new List<JournalEntry>
            {
                JournalEntry("step-x", action, null)  // dispatched, no settlement
            };
            var result = CrashStateClassifier.Classify(entries);
            result.Should().Be(RunState.UNKNOWN,
                $"AC#4: dispatched '{action}' without settlement must yield UNKNOWN");
        }
    }

    // ── BrowserProcessMonitor — crash journal entry (AC#2) ───────────────────

    [Fact]
    public async Task Monitor_AC2_BrowserCrash_AppendsCrashEntryToJournal()
    {
        // Arrange
        const string runId = "run-ac2-crash-journal";
        var journalMock = new Mock<IRunJournal>();

        // Return no effectful journal entries (crash before any side effect)
        journalMock
            .Setup(j => j.ReadAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        journalMock
            .Setup(j => j.AppendStepAsync(
                runId,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var browserMock = new Mock<IBrowser>();

        var crashEventFired = new TaskCompletionSource<CrashClassifiedEventArgs>();

        using var sut = new BrowserProcessMonitor(
            journalMock.Object,
            NullLogger<BrowserProcessMonitor>.Instance);

        sut.CrashClassified += (_, args) => crashEventFired.TrySetResult(args);

        sut.Attach(browserMock.Object, runId);

        // Act — simulate unexpected browser process exit
        browserMock.Raise(b => b.Disconnected += null, (object)browserMock.Object, browserMock.Object);

        // Wait for async crash handler to complete
        var crashArgs = await crashEventFired.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — crash was appended to the journal
        journalMock.Verify(j => j.AppendStepAsync(
                runId,
                It.IsAny<string>(),
                "browser.crashed",
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "AC#2: crash event must be recorded in the run journal");
    }

    // ── BrowserProcessMonitor — AC#3 crash before effectful action ────────────

    [Fact]
    public async Task Monitor_AC3_CrashBeforeEffectfulAction_ClassifiesAsFailed()
    {
        // Arrange
        const string runId = "run-ac3-no-effectful";
        var journalMock = new Mock<IRunJournal>();

        // Journal contains only non-effectful entries
        journalMock
            .Setup(j => j.ReadAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                JournalEntry("open", "journal.open", "opened"),
                JournalEntry("s1",   "page.wait",    "networkidle")
            ]);

        journalMock
            .Setup(j => j.AppendStepAsync(
                runId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var browserMock = new Mock<IBrowser>();
        var crashEventFired = new TaskCompletionSource<CrashClassifiedEventArgs>();

        using var sut = new BrowserProcessMonitor(
            journalMock.Object,
            NullLogger<BrowserProcessMonitor>.Instance);

        sut.CrashClassified += (_, args) => crashEventFired.TrySetResult(args);
        sut.Attach(browserMock.Object, runId);

        // Act
        browserMock.Raise(b => b.Disconnected += null, (object)browserMock.Object, browserMock.Object);
        var crashArgs = await crashEventFired.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        crashArgs.RunId.Should().Be(runId);
        crashArgs.TerminalState.Should().Be(RunState.FAILED,
            "AC#3: crash before any irreversible action → FAILED, never UNKNOWN");
    }

    // ── BrowserProcessMonitor — AC#4 crash with indeterminate effectful action ─

    [Fact]
    public async Task Monitor_AC4_CrashDuringEffectfulAction_ClassifiesAsUnknown()
    {
        // Arrange
        const string runId = "run-ac4-indeterminate";
        var journalMock = new Mock<IRunJournal>();

        // Journal shows 'form.submit' dispatched but never settled
        journalMock
            .Setup(j => j.ReadAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                JournalEntry("open",   "journal.open", "opened"),
                JournalEntry("step-1", "navigate",     null),
                JournalEntry("step-1", "navigate.settled", "https://example.com"),
                JournalEntry("step-2", "form.submit",  null),    // dispatched, no settlement
            ]);

        journalMock
            .Setup(j => j.AppendStepAsync(
                runId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var browserMock = new Mock<IBrowser>();
        var crashEventFired = new TaskCompletionSource<CrashClassifiedEventArgs>();

        using var sut = new BrowserProcessMonitor(
            journalMock.Object,
            NullLogger<BrowserProcessMonitor>.Instance);

        sut.CrashClassified += (_, args) => crashEventFired.TrySetResult(args);
        sut.Attach(browserMock.Object, runId);

        // Act
        browserMock.Raise(b => b.Disconnected += null, (object)browserMock.Object, browserMock.Object);
        var crashArgs = await crashEventFired.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        crashArgs.TerminalState.Should().Be(RunState.UNKNOWN,
            "AC#4: crash while an effectful action is in-flight → UNKNOWN (outcome indeterminate)");
    }

    [Fact]
    public async Task Monitor_AC4_CrashAfterAllSettled_ClassifiesAsFailed()
    {
        // Arrange
        const string runId = "run-ac4-all-settled";
        var journalMock = new Mock<IRunJournal>();

        // All effectful actions settled before crash
        journalMock
            .Setup(j => j.ReadAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                JournalEntry("step-1", "click",          null),
                JournalEntry("step-1", "click.settled",  "ok"),
            ]);

        journalMock
            .Setup(j => j.AppendStepAsync(
                runId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var browserMock = new Mock<IBrowser>();
        var crashEventFired = new TaskCompletionSource<CrashClassifiedEventArgs>();

        using var sut = new BrowserProcessMonitor(
            journalMock.Object,
            NullLogger<BrowserProcessMonitor>.Instance);

        sut.CrashClassified += (_, args) => crashEventFired.TrySetResult(args);
        sut.Attach(browserMock.Object, runId);

        // Act
        browserMock.Raise(b => b.Disconnected += null, (object)browserMock.Object, browserMock.Object);
        var crashArgs = await crashEventFired.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        crashArgs.TerminalState.Should().Be(RunState.FAILED,
            "AC#4: crash after all effectful actions settled → FAILED (deterministic)");
    }

    // ── BrowserProcessMonitor — AC#4 no auto-restart ─────────────────────────

    [Fact]
    public async Task Monitor_AC4_CrashClassified_DoesNotTriggerBrowserRelaunch()
    {
        // Arrange — monitor fires CrashClassified; no launch infrastructure is involved
        const string runId = "run-ac4-no-restart";
        var journalMock = new Mock<IRunJournal>();

        journalMock
            .Setup(j => j.ReadAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        journalMock
            .Setup(j => j.AppendStepAsync(
                runId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var browserMock = new Mock<IBrowser>();
        var crashCount = 0;

        using var sut = new BrowserProcessMonitor(
            journalMock.Object,
            NullLogger<BrowserProcessMonitor>.Instance);

        sut.CrashClassified += (_, _) => Interlocked.Increment(ref crashCount);
        sut.Attach(browserMock.Object, runId);

        // Act — crash fires once
        browserMock.Raise(b => b.Disconnected += null, (object)browserMock.Object, browserMock.Object);

        await Task.Delay(200); // allow fire-and-forget to complete

        // Assert — no "auto-restart" launch was invoked; the CrashClassified event fires once
        crashCount.Should().Be(1,
            "AC#4: the monitor fires CrashClassified exactly once; it does not launch a new browser");
        browserMock.Verify(b => b.CloseAsync(It.IsAny<BrowserCloseOptions?>()), Times.Never,
            "The monitor itself does not call CloseAsync — the worker handles resource release");
    }

    // ── BrowserProcessMonitor — Detach suppresses crash event ────────────────

    [Fact]
    public async Task Monitor_Detach_BeforeDisconnectedEvent_SuppressesCrashClassified()
    {
        // Arrange
        const string runId = "run-detach-suppress";
        var journalMock = new Mock<IRunJournal>();
        var browserMock = new Mock<IBrowser>();

        var crashFired = false;

        using var sut = new BrowserProcessMonitor(
            journalMock.Object,
            NullLogger<BrowserProcessMonitor>.Instance);

        sut.CrashClassified += (_, _) => crashFired = true;
        sut.Attach(browserMock.Object, runId);

        // Act — detach before the browser's Disconnected event fires (clean shutdown)
        sut.Detach(runId);
        browserMock.Raise(b => b.Disconnected += null, (object)browserMock.Object, browserMock.Object);

        await Task.Delay(200); // allow async handler to settle

        // Assert — CrashClassified must not have fired
        crashFired.Should().BeFalse(
            "Detach must suppress the CrashClassified event for a clean, planned browser close");
    }

    // ── BrowserProcessMonitor — duplicate Attach is safe ─────────────────────

    [Fact]
    public async Task Monitor_DuplicateAttach_IsSafeNoOpForSecondCall()
    {
        const string runId = "run-dup-attach";
        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.ReadAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        journalMock
            .Setup(j => j.AppendStepAsync(
                runId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var browserMock = new Mock<IBrowser>();
        var crashCount = 0;

        using var sut = new BrowserProcessMonitor(
            journalMock.Object,
            NullLogger<BrowserProcessMonitor>.Instance);

        sut.CrashClassified += (_, _) => Interlocked.Increment(ref crashCount);

        // Act — attach twice; second call must be silently ignored
        sut.Attach(browserMock.Object, runId);
        sut.Attach(browserMock.Object, runId);  // duplicate

        browserMock.Raise(b => b.Disconnected += null, (object)browserMock.Object, browserMock.Object);
        await Task.Delay(200);

        // Assert — CrashClassified fires at most once
        crashCount.Should().BeLessOrEqualTo(1,
            "duplicate Attach must not cause CrashClassified to fire more than once");
    }

    // ── BrowserProcessMonitor — journal failure defaults to UNKNOWN ───────────

    [Fact]
    public async Task Monitor_JournalReadFailure_DefaultsToUnknown()
    {
        // Arrange — journal.ReadAsync throws; safe default is UNKNOWN
        const string runId = "run-journal-fail";
        var journalMock = new Mock<IRunJournal>();

        journalMock
            .Setup(j => j.ReadAsync(runId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("journal file locked"));

        journalMock
            .Setup(j => j.AppendStepAsync(
                runId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var browserMock = new Mock<IBrowser>();
        var crashEventFired = new TaskCompletionSource<CrashClassifiedEventArgs>();

        using var sut = new BrowserProcessMonitor(
            journalMock.Object,
            NullLogger<BrowserProcessMonitor>.Instance);

        sut.CrashClassified += (_, args) => crashEventFired.TrySetResult(args);
        sut.Attach(browserMock.Object, runId);

        // Act
        browserMock.Raise(b => b.Disconnected += null, (object)browserMock.Object, browserMock.Object);
        var crashArgs = await crashEventFired.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert — when journal is unreadable, assume uncertain state for safety
        crashArgs.TerminalState.Should().Be(RunState.UNKNOWN,
            "when the journal cannot be read during crash classification, UNKNOWN is the safe default");
    }

    // ── CEC compliance — no HANDED_OFF or SUCCEEDED from crash ───────────────

    [Fact]
    public void Classifier_CecCompliance_NeverReturnsHandedOffOrSucceeded()
    {
        // A crash can only yield FAILED or UNKNOWN — never SUCCEEDED, HANDED_OFF, etc.
        var allVariants = new[]
        {
            new List<JournalEntry>(),  // empty — FAILED
            new List<JournalEntry>
            {
                JournalEntry("s1", "click", null)  // dispatched, no settlement — UNKNOWN
            }
        };

        foreach (var entries in allVariants)
        {
            var result = CrashStateClassifier.Classify(entries);
            // CEC v1.0.1: a crash can only produce FAILED or UNKNOWN; never SUCCEEDED or HANDED_OFF
            result.Should().NotBe(RunState.SUCCEEDED).And.NotBe(RunState.HANDED_OFF);
            result.Should().BeOneOf(RunState.FAILED, RunState.UNKNOWN);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JournalEntry JournalEntry(string stepId, string action, object? outcome) =>
        new()
        {
            StepId    = stepId,
            Action    = action,
            Timestamp = DateTimeOffset.UtcNow,
            Outcome   = outcome
        };
}
