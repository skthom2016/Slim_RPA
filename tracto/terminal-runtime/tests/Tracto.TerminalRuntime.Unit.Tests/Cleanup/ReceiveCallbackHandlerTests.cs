using FluentAssertions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Cleanup;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Cleanup;

namespace Tracto.TerminalRuntime.Unit.Tests.Cleanup;

/// <summary>
/// Unit tests for TER-US-019 AC-3 / AC-4: continuation run (Run B) creation rules.
///
/// AC-3 invariants:
///   Auth:  Unauthenticated callback → refused before any state is read.
///   Dedup: Duplicate CallbackId → idempotent Ok, no second Run B created.
///
/// AC-4 invariants:
///   1. Dynamic scriptRef in callback payload → refused.
///   2. Source run NOT in HANDED_OFF → Run B is not created.
///   3. Run B created only after HANDED_OFF confirmed; never speculatively.
///   4. ScriptRef always comes from static mapping, never from callback payload.
/// </summary>
public sealed class ReceiveCallbackHandlerTests
{
    private static IClock FixedClock()
    {
        var mock = new Mock<IClock>();
        mock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        return mock.Object;
    }

    private static IStaticContinuationMappingStore MappingStore(
        string mappingRef, string scriptRef)
    {
        var mock = new Mock<IStaticContinuationMappingStore>();
        mock.Setup(s => s.GetMappingAsync(mappingRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StaticContinuationMapping, Error>.Ok(
                new StaticContinuationMapping(mappingRef, scriptRef)));
        return mock.Object;
    }

    private static ICallbackAuthenticator AuthenticatorOk()
    {
        var mock = new Mock<ICallbackAuthenticator>();
        mock.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));
        return mock.Object;
    }

    private static ICallbackAuthenticator AuthenticatorFail()
    {
        var mock = new Mock<ICallbackAuthenticator>();
        mock.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Fail(
                Error.Authentication("Callback.Unauthenticated", "Credential mismatch.")));
        return mock.Object;
    }

    private static IRunJournal JournalWithState(string runId, RunState state)
    {
        var mock = new Mock<IRunJournal>();
        mock.Setup(j => j.GetStateAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RunState, Error>.Ok(state));
        mock.Setup(j => j.WriteEntryAsync(It.IsAny<RunJournalEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));
        mock.Setup(j => j.HasProcessedCallbackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(false));
        mock.Setup(j => j.RecordProcessedCallbackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));
        return mock.Object;
    }

    // ── AC-3(auth): unauthenticated callback → refused before any state read ──

    [Fact]
    public async Task HandleAsync_UnauthenticatedCallback_Refused_BeforeReadingRunState()
    {
        // Arrange
        var command = new ReceiveCallbackCommand(
            CallbackId:                   "cb-unauth",
            CallbackHmac:                 "bad-hmac",
            SourceRunId:                  "run-auth-fail",
            ContinuationMappingRef:       "map-001",
            DynamicScriptRefFromCallback: null,
            ServicePayload:               null);

        var journalMock = new Mock<IRunJournal>();
        var handler = new ReceiveCallbackHandler(
            journalMock.Object,
            new Mock<IStaticContinuationMappingStore>().Object,
            AuthenticatorFail(),
            FixedClock());

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Callback.Unauthenticated",
            "unauthenticated callback must be refused as the very first guard");
        result.Error.Kind.Should().Be(ErrorKind.Authentication);

        // The journal must never be queried — a spoofed request must not leak
        // information about the existence of source runs.
        journalMock.Verify(
            j => j.GetStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "journal must not be queried for unauthenticated requests");
        journalMock.Verify(
            j => j.WriteEntryAsync(It.IsAny<RunJournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── AC-3(dedup): duplicate CallbackId → idempotent Ok, no second Run B ──

    [Fact]
    public async Task HandleAsync_DuplicateCallbackId_ReturnsIdempotentOk_NoSecondRunB()
    {
        // Arrange
        const string sourceRunId = "run-handedoff-dedup";
        const string callbackId  = "cb-dup-001";

        var command = new ReceiveCallbackCommand(
            CallbackId:                   callbackId,
            CallbackHmac:                 "hmac-test-value",
            SourceRunId:                  sourceRunId,
            ContinuationMappingRef:       "map-dedup-001",
            DynamicScriptRefFromCallback: null,
            ServicePayload:               null);

        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.GetStateAsync(sourceRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RunState, Error>.Ok(RunState.HandedOff));
        // CallbackId already seen → deduplication returns true.
        journalMock
            .Setup(j => j.HasProcessedCallbackAsync(sourceRunId, callbackId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new ReceiveCallbackHandler(
            journalMock.Object,
            new Mock<IStaticContinuationMappingStore>().Object,
            AuthenticatorOk(),
            FixedClock());

        // Act
        var result = await handler.HandleAsync(command);

        // Assert — idempotent 200: success with no new Run B.
        result.IsSuccess.Should().BeTrue();
        result.Value.ContinuationRunCreated.Should().BeFalse(
            "a duplicate callback must not create a second Run B");
        result.Value.ContinuationRunId.Should().BeNull();
        result.Value.ContinuationScriptRef.Should().BeNull();

        journalMock.Verify(
            j => j.WriteEntryAsync(It.IsAny<RunJournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "duplicate callback must not write a second Run B to the journal");
        journalMock.Verify(
            j => j.RecordProcessedCallbackAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "RecordProcessedCallbackAsync must not be called when the callback was already recorded");
    }

    // ── AC-4(a): dynamic scriptRef in callback → refused ────────────────────

    [Fact]
    public async Task HandleAsync_DynamicScriptRefInCallback_Refused()
    {
        // Arrange
        const string sourceRunId  = "run-src-001";
        const string mappingRef   = "map-001";
        const string dynamicRef   = "script://dynamic/injected";

        var command = new ReceiveCallbackCommand(
            CallbackId:                  "cb-001",
            CallbackHmac:                "hmac-test-value",
            SourceRunId:                 sourceRunId,
            ContinuationMappingRef:      mappingRef,
            DynamicScriptRefFromCallback: dynamicRef,
            ServicePayload:              null);

        var journal      = new Mock<IRunJournal>();
        var mappingStore = new Mock<IStaticContinuationMappingStore>();
        var handler = new ReceiveCallbackHandler(
            journal.Object, mappingStore.Object, AuthenticatorOk(), FixedClock());

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Callback.DynamicScriptRefRefused",
            "any scriptRef arriving in the callback payload must be refused");

        // No Run B must have been created.
        journal.Verify(
            j => j.WriteEntryAsync(It.IsAny<RunJournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── AC-4(b): source run not HANDED_OFF → Run B is not created ───────────

    [Theory]
    [InlineData(RunState.Running)]
    [InlineData(RunState.Queued)]
    [InlineData(RunState.Leased)]
    [InlineData(RunState.Succeeded)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Cancelled)]
    [InlineData(RunState.Unknown)]
    public async Task HandleAsync_SourceRunNotHandedOff_RunBNotCreated(RunState state)
    {
        // Arrange
        const string sourceRunId = "run-not-handedoff";
        const string mappingRef  = "map-001";

        var command = new ReceiveCallbackCommand(
            CallbackId:                   "cb-002",
            CallbackHmac:                 "hmac-test-value",
            SourceRunId:                  sourceRunId,
            ContinuationMappingRef:       mappingRef,
            DynamicScriptRefFromCallback: null,
            ServicePayload:               null);

        var journal      = JournalWithState(sourceRunId, state);
        var journalMock  = Mock.Get(journal);
        var mappingStore = new Mock<IStaticContinuationMappingStore>();
        var handler      = new ReceiveCallbackHandler(journal, mappingStore.Object, AuthenticatorOk(), FixedClock());

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Callback.SourceNotHandedOff",
            "Run B must not be created until source is HANDED_OFF");

        journalMock.Verify(
            j => j.WriteEntryAsync(It.IsAny<RunJournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── AC-4(c): HANDED_OFF confirmed → Run B created with static scriptRef ──

    [Fact]
    public async Task HandleAsync_SourceRunHandedOff_CreatesRunBWithStaticScriptRef()
    {
        // Arrange
        const string sourceRunId = "run-handedoff-001";
        const string mappingRef  = "map-static-001";
        const string staticRef   = "script://approved/continuation-v1";

        var command = new ReceiveCallbackCommand(
            CallbackId:                   "cb-003",
            CallbackHmac:                 "hmac-test-value",
            SourceRunId:                  sourceRunId,
            ContinuationMappingRef:       mappingRef,
            DynamicScriptRefFromCallback: null,
            ServicePayload:               "{}");

        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.GetStateAsync(sourceRunId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RunState, Error>.Ok(RunState.HandedOff));
        journalMock
            .Setup(j => j.HasProcessedCallbackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(false));
        journalMock
            .Setup(j => j.RecordProcessedCallbackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        RunJournalEntry? capturedEntry = null;
        journalMock
            .Setup(j => j.WriteEntryAsync(It.IsAny<RunJournalEntry>(), It.IsAny<CancellationToken>()))
            .Callback<RunJournalEntry, CancellationToken>((e, _) => capturedEntry = e)
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var handler = new ReceiveCallbackHandler(
            journalMock.Object,
            MappingStore(mappingRef, staticRef),
            AuthenticatorOk(),
            FixedClock());

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ContinuationRunCreated.Should().BeTrue();
        result.Value.ContinuationRunId.Should().NotBeNullOrEmpty();
        result.Value.ContinuationScriptRef.Should().Be(staticRef,
            "scriptRef must come from static mapping, not from callback");

        // Verify Run B was written to the journal in QUEUED state.
        capturedEntry.Should().NotBeNull();
        capturedEntry!.State.Should().Be(RunState.Queued,
            "Run B starts as QUEUED — it is not speculatively set to a later state");
        capturedEntry.FinalResult.Should().BeNull("Run B is not yet complete");

        // Verify deduplication marker was persisted (before Run B creation).
        journalMock.Verify(
            j => j.RecordProcessedCallbackAsync(
                sourceRunId, "cb-003", It.IsAny<CancellationToken>()),
            Times.Once,
            "CallbackId must be persisted to prevent duplicate Run B creation on retry");
    }

    // ── AC-4(d): no speculative Run B at HANDED_OFF time ────────────────────

    [Fact]
    public async Task HandleAsync_SpeculativeCreation_IsNotPossible_WithoutCallback()
    {
        // This test documents that ReceiveCallbackHandler is the only path that
        // creates Run B.  ServiceHandoffHandler (the HANDED_OFF writer) must NOT
        // write a continuation run entry.  We verify this by asserting that
        // ServiceHandoffHandler never calls WriteEntryAsync.
        const string runId   = "run-speculative-check";
        const string attempt = "attempt-spec";
        const string lease   = "lease-spec";

        var journalMock = new Mock<IRunJournal>();
        journalMock
            .Setup(j => j.RecordStateAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                RunState.HandedOff, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<bool, Error>.Ok(true));

        var clockMock = new Mock<IClock>();
        clockMock.SetupGet(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var handoffHandler = new Infrastructure.Cleanup.ServiceHandoffHandler(
            journalMock.Object,
            clockMock.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Infrastructure.Cleanup.ServiceHandoffHandler>.Instance);

        // Act — handoff the run
        var handoffResult = await handoffHandler.HandleHandoffAsync(
            runId, attempt, lease, continuationMappingRef: "map-spec");

        // Assert — HANDED_OFF persisted, but NO new RunJournalEntry written (WriteEntryAsync never called).
        handoffResult.IsSuccess.Should().BeTrue();
        journalMock.Verify(
            j => j.WriteEntryAsync(It.IsAny<RunJournalEntry>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Run B must NOT be created speculatively at HANDED_OFF time — " +
            "only ReceiveCallbackHandler creates Run B upon eligible callback");
    }
}
