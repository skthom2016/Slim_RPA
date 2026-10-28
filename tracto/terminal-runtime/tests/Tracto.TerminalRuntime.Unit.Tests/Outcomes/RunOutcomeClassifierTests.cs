using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracto.TerminalRuntime.Application.Outcomes;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Outcomes;
using Tracto.TerminalRuntime.Infrastructure.Outcomes;

namespace Tracto.TerminalRuntime.Unit.Tests.Outcomes;

/// <summary>
/// Unit tests for TER-US-016: Unknown Side Effect Classification and No Duplicate Replay.
///
/// Acceptance Criteria covered:
///   AC-1: Read-only transient failure → retried when attempt count below limit.
///   AC-2: Irreversible + write evidence + no exit code → UNKNOWN + needsAttention=true + unknownReason.
///   AC-3: No auto-replay after UNKNOWN (ShouldRetry=false).
///   AC-4: UNKNOWN reported with needsAttention=true.
/// </summary>
public sealed class RunOutcomeClassifierTests
{
    private readonly RunOutcomeClassifier _sut =
        new(NullLogger<RunOutcomeClassifier>.Instance);

    // ── AC-2 + AC-3 + AC-4: Irreversible + write evidence + process killed ────

    [Fact]
    public async Task IrreversibleCommand_WriteEvidence_ProcessKilled_Yields_Unknown_NeedsAttention()
    {
        // Arrange
        var command = MakeCommand(
            idempotencyClass: IdempotencyClass.Irreversible,
            stdout:           "Record inserted successfully",
            exitCodeReceived: false,
            failureMode:      ExecutionFailureMode.ProcessKilled);

        // Act
        var result = await _sut.ClassifyAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var outcome = result.Value;

        outcome.State.Should().Be(RunState.Unknown,
            because: "irreversible command with write evidence and no exit receipt must become UNKNOWN");
        outcome.NeedsAttention.Should().BeTrue(
            because: "CEC v1.0.1 invariant #2: UNKNOWN always needs attention");
        outcome.UnknownReason.Should().Be(UnknownReason.ProcessKilledAfterWrite);
        outcome.ErrorCategory.Should().Be(ErrorCategory.Internal);
        outcome.ShouldRetry.Should().BeFalse(
            because: "CEC v1.0.1 invariant #3: irreversible commands must not be auto-replayed");
        outcome.SatisfiesInvariant().Should().BeTrue();
    }

    [Fact]
    public async Task IrreversibleCommand_WriteEvidence_TransportLost_Yields_Unknown_NeedsAttention()
    {
        var command = MakeCommand(
            idempotencyClass: IdempotencyClass.Irreversible,
            stdout:           "rows affected: 42",
            exitCodeReceived: false,
            failureMode:      ExecutionFailureMode.TransportLost);

        var result = await _sut.ClassifyAsync(command);

        result.IsSuccess.Should().BeTrue();
        var outcome = result.Value;

        outcome.State.Should().Be(RunState.Unknown);
        outcome.NeedsAttention.Should().BeTrue();
        outcome.UnknownReason.Should().Be(UnknownReason.TransportLostAfterWrite);
        outcome.ErrorCategory.Should().Be(ErrorCategory.Transport);
        outcome.ShouldRetry.Should().BeFalse();
    }

    [Fact]
    public async Task IrreversibleCommand_WriteEvidence_Timeout_Yields_Unknown_NeedsAttention()
    {
        var command = MakeCommand(
            idempotencyClass: IdempotencyClass.Irreversible,
            stdout:           "Deployed successfully",
            exitCodeReceived: false,
            failureMode:      ExecutionFailureMode.Timeout);

        var result = await _sut.ClassifyAsync(command);

        var outcome = result.Value;
        outcome.State.Should().Be(RunState.Unknown);
        outcome.NeedsAttention.Should().BeTrue();
        outcome.UnknownReason.Should().Be(UnknownReason.TimeoutDuringIrreversibleWrite);
        outcome.ErrorCategory.Should().Be(ErrorCategory.Timeout);
        outcome.ShouldRetry.Should().BeFalse();
    }

    // ── AC-3: No second attempt after UNKNOWN ────────────────────────────────

    [Fact]
    public async Task IrreversibleCommand_AfterUnknown_ShouldRetry_Is_False()
    {
        // Even if we try to classify again with attempt count 1 and MaxRetries 5,
        // an irreversible command that produced UNKNOWN must never carry ShouldRetry=true.
        var command = MakeCommand(
            idempotencyClass: IdempotencyClass.Irreversible,
            stdout:           "committed",
            exitCodeReceived: false,
            failureMode:      ExecutionFailureMode.ProcessKilled,
            attemptCount:     1,
            maxRetries:       5);

        var result = await _sut.ClassifyAsync(command);

        result.Value.ShouldRetry.Should().BeFalse(
            because: "irreversible commands must never be auto-replayed after UNKNOWN");
        result.Value.State.Should().Be(RunState.Unknown);
    }

    // ── AC-1: Read-only transient failure → retry ────────────────────────────

    [Fact]
    public async Task ReadOnly_TransientError_BelowRetryLimit_Yields_RetriableFailure()
    {
        var command = MakeCommand(
            idempotencyClass: IdempotencyClass.ReadOnly,
            stdout:           string.Empty,
            exitCodeReceived: false,
            failureMode:      ExecutionFailureMode.TransientError,
            attemptCount:     1,
            maxRetries:       3);

        var result = await _sut.ClassifyAsync(command);

        result.IsSuccess.Should().BeTrue();
        var outcome = result.Value;

        outcome.ShouldRetry.Should().BeTrue(
            because: "read-only transient failures below the retry limit must be retried (AC-1)");
        outcome.State.Should().Be(RunState.Running,
            because: "the run is not yet terminal when a retry is scheduled");
        outcome.NeedsAttention.Should().BeFalse();
        outcome.UnknownReason.Should().BeNull();
    }

    [Fact]
    public async Task Idempotent_TransientError_BelowRetryLimit_Yields_RetriableFailure()
    {
        // Idempotent commands (e.g. upsert, PUT) must be retried on transient errors,
        // just like ReadOnly commands, because re-executing them produces the same net result.
        // Fixing MAJOR review finding: Rule 2 previously excluded IdempotencyClass.Idempotent,
        // causing these commands to fall through to Rule 4 (FAILED, no retry) on network blips.
        var command = MakeCommand(
            idempotencyClass: IdempotencyClass.Idempotent,
            stdout:           string.Empty,
            exitCodeReceived: false,
            failureMode:      ExecutionFailureMode.TransientError,
            attemptCount:     1,
            maxRetries:       3);

        var result = await _sut.ClassifyAsync(command);

        result.IsSuccess.Should().BeTrue();
        var outcome = result.Value;

        outcome.ShouldRetry.Should().BeTrue(
            because: "idempotent transient failures below the retry limit must be retried (AC-1)");
        outcome.State.Should().Be(RunState.Running,
            because: "the run is not yet terminal when a retry is scheduled");
        outcome.NeedsAttention.Should().BeFalse();
        outcome.UnknownReason.Should().BeNull();
    }

    [Fact]
    public async Task ReadOnly_TransientError_AtRetryLimit_Yields_Failed_NoRetry()
    {
        var command = MakeCommand(
            idempotencyClass: IdempotencyClass.ReadOnly,
            stdout:           string.Empty,
            exitCodeReceived: false,
            failureMode:      ExecutionFailureMode.TransientError,
            attemptCount:     3,
            maxRetries:       3);  // AttemptCount == MaxRetries → no more retries

        var result = await _sut.ClassifyAsync(command);

        var outcome = result.Value;
        outcome.ShouldRetry.Should().BeFalse();
        outcome.State.Should().NotBe(RunState.Unknown);
    }

    // ── Clean exit codes ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExitCode_Zero_Yields_Succeeded()
    {
        var command = MakeCommand(
            idempotencyClass: IdempotencyClass.Irreversible,
            stdout:           "committed",
            exitCodeReceived: true,
            exitCode:         0,
            failureMode:      ExecutionFailureMode.None);

        var result = await _sut.ClassifyAsync(command);

        result.Value.State.Should().Be(RunState.Succeeded);
        result.Value.ShouldRetry.Should().BeFalse();
        result.Value.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public async Task ExitCode_NonZero_Yields_Failed()
    {
        var command = MakeCommand(
            idempotencyClass: IdempotencyClass.ReadOnly,
            stdout:           "Error: not found",
            exitCodeReceived: true,
            exitCode:         1,
            failureMode:      ExecutionFailureMode.None);

        var result = await _sut.ClassifyAsync(command);

        result.Value.State.Should().Be(RunState.Failed);
        result.Value.ShouldRetry.Should().BeFalse();
    }

    // ── AC-4: UNKNOWN reported to Orchestrator with needsAttention=true ───────

    [Theory]
    [InlineData(ExecutionFailureMode.TransportLost)]
    [InlineData(ExecutionFailureMode.ProcessKilled)]
    [InlineData(ExecutionFailureMode.Timeout)]
    public async Task Unknown_Outcome_Always_Has_NeedsAttention_True(ExecutionFailureMode mode)
    {
        var command = MakeCommand(
            idempotencyClass: IdempotencyClass.Irreversible,
            stdout:           "saved",
            exitCodeReceived: false,
            failureMode:      mode);

        var result = await _sut.ClassifyAsync(command);

        result.Value.NeedsAttention.Should().BeTrue(
            because: "UNKNOWN runs must always be surfaced to a human operator (AC-4)");
        result.Value.UnknownReason.Should().NotBeNull(
            because: "CEC v1.0.1 invariant #2: UNKNOWN must carry a reason code");
    }

    // ── Irreversible command, no write evidence → not UNKNOWN ────────────────

    [Fact]
    public async Task IrreversibleCommand_NoWriteEvidence_TransportLost_Yields_Failed_NotUnknown()
    {
        // No write evidence in stdout → we cannot confirm the side-effect was applied,
        // so we classify as FAILED (not UNKNOWN) — operator still sees the failure.
        var command = MakeCommand(
            idempotencyClass: IdempotencyClass.Irreversible,
            stdout:           "Starting operation...",  // no write indicator
            exitCodeReceived: false,
            failureMode:      ExecutionFailureMode.TransportLost);

        var result = await _sut.ClassifyAsync(command);

        result.Value.State.Should().NotBe(RunState.Unknown,
            because: "without write evidence we cannot confirm an irreversible side-effect occurred");
        result.Value.ShouldRetry.Should().BeFalse();
    }

    // ── StdoutIndicatesWrite heuristic ────────────────────────────────────────

    [Theory]
    [InlineData("Record inserted successfully", true)]
    [InlineData("rows affected: 5", true)]
    [InlineData("committed", true)]
    [InlineData("Deployed successfully to prod", true)]
    [InlineData("uploaded 42 files", true)]
    [InlineData("Starting operation...", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Error: permission denied", false)]
    public void StdoutIndicatesWrite_Returns_Expected(string? stdout, bool expected)
    {
        RunOutcomeClassifier.StdoutIndicatesWrite(stdout).Should().Be(expected);
    }

    // ── Null guard ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Null_Command_Throws_ArgumentNullException()
    {
        var act = async () => await _sut.ClassifyAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── RunOutcome invariant ──────────────────────────────────────────────────

    [Fact]
    public void RunOutcome_Unknown_Without_Reason_Fails_Invariant()
    {
        // Manually constructing a broken UNKNOWN outcome must fail the invariant check.
        var broken = new RunOutcome(
            RunState.Unknown,
            NeedsAttention: false,   // violates invariant #2
            UnknownReason: null,     // violates invariant #2
            ErrorCategory: null,
            ShouldRetry: false);

        broken.SatisfiesInvariant().Should().BeFalse();
    }

    [Fact]
    public void RunOutcome_Unknown_With_Reason_Satisfies_Invariant()
    {
        var valid = RunOutcome.UnknownNoRetry(
            UnknownReason.TransportLostAfterWrite,
            ErrorCategory.Transport);

        valid.SatisfiesInvariant().Should().BeTrue();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ClassifyRunOutcomeCommand MakeCommand(
        IdempotencyClass     idempotencyClass,
        string               stdout,
        bool                 exitCodeReceived,
        ExecutionFailureMode failureMode,
        int?                 exitCode    = null,
        int                  attemptCount = 1,
        int                  maxRetries   = 3)
    {
        return new ClassifyRunOutcomeCommand(
            RunId:            "run-001",
            AttemptId:        "attempt-001",
            IdempotencyClass: idempotencyClass,
            StdoutContent:    stdout,
            ExitCodeReceived: exitCodeReceived,
            ExitCode:         exitCode,
            FailureMode:      failureMode,
            AttemptCount:     attemptCount,
            MaxRetries:       maxRetries);
    }
}
