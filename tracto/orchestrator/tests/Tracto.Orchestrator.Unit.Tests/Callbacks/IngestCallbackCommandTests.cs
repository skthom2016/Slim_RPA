#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Callbacks;
using Tracto.Orchestrator.Domain.Callbacks;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Infrastructure.Clock;

namespace Tracto.Orchestrator.Unit.Tests.Callbacks;

/// <summary>
/// Unit tests for <see cref="IngestCallbackCommandHandler"/> — story OC-07.
/// Each test covers exactly one observable behaviour.
/// FakeClock controls time; Moq stubs all external dependencies.
///
/// Handler execution order:
///   1. Forbidden payload field check  → 400 Validation
///   2. HMAC validation                → 401 INVALID_HMAC + SecurityEvent
///   3. Replay-window check            → 401 REPLAY_DETECTED + SecurityEvent
///   4. Duplicate EventId              → 200 DUPLICATE (idempotent)
///   5. CorrelationId lookup           → 200 CorrelationNotFound (when absent)
///   6. Run state guard (HANDED_OFF)   → 422 NOT_HANDED_OFF
///   7. Atomic write                   → 200 Accepted
/// </summary>
public sealed class IngestCallbackCommandTests
{
    // ── Shared baselines ──────────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private const string ValidKeyId       = "key-oc07";
    private const string ValidNonce       = "nonce-abc123";
    private const string ValidSignature   = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef";
    private const string ValidPayload     = """{"status":"ok"}""";
    private const string ValidEventId     = "evt-oc07-001";

    // Timestamp exactly at FixedNow (well within the 5-minute replay window).
    private static string FreshTimestamp => FixedNow.ToString("O");

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates a fully-wired handler with Moq-backed dependencies.
    /// Defaults:
    ///   - HMAC validator returns valid.
    ///   - No existing CallbackRecord (no duplicate).
    ///   - No run found by CorrelationId (returns null).
    ///   - UnitOfWork commits successfully.
    /// </summary>
    private static IngestCallbackCommandHandler MakeHandler(
        out Mock<ICallbackRepository>  callbackRepo,
        out Mock<IRunRepository>       runRepo,
        out Mock<IJournalRepository>   journalRepo,
        out Mock<ISecurityEventLog>    securityLog,
        out Mock<IHmacValidator>       hmacValidator,
        out Mock<IContinuationQueue>   continuationQueue,
        out Mock<IUnitOfWork>          unitOfWork,
        FakeClock?                     clock         = null,
        bool                           hmacValid     = true,
        CallbackRecord?                existingCallback = null,
        RunRecord?                     existingRun   = null)
    {
        callbackRepo      = new Mock<ICallbackRepository>(MockBehavior.Strict);
        runRepo           = new Mock<IRunRepository>(MockBehavior.Strict);
        journalRepo       = new Mock<IJournalRepository>(MockBehavior.Strict);
        securityLog       = new Mock<ISecurityEventLog>(MockBehavior.Strict);
        hmacValidator     = new Mock<IHmacValidator>(MockBehavior.Strict);
        continuationQueue = new Mock<IContinuationQueue>(MockBehavior.Strict);
        unitOfWork        = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var fakeClock = clock ?? new FakeClock(FixedNow);

        // HMAC validator default.
        hmacValidator
            .Setup(h => h.Validate(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(hmacValid);

        // Callback dedup: no existing record by default.
        callbackRepo
            .Setup(r => r.FindByEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCallback);

        // Run lookup: no run by default.
        runRepo
            .Setup(r => r.FindByCorrelationIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRun);

        // UnitOfWork lifecycle.
        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync())
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.DisposeAsync())
                  .Returns(ValueTask.CompletedTask);

        // Security event log: lenient by default (tests that don't care about events).
        securityLog
            .Setup(s => s.AppendAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(t => t.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        return new IngestCallbackCommandHandler(
            callbackRepo.Object,
            runRepo.Object,
            journalRepo.Object,
            securityLog.Object,
            hmacValidator.Object,
            continuationQueue.Object,
            unitOfWork.Object,
            fakeClock,
            telemetry.Object,
            NullLogger<IngestCallbackCommandHandler>.Instance);
    }

    /// <summary>Builds a valid <see cref="IngestCallbackCommand"/> with all fields set.</summary>
    private static IngestCallbackCommand ValidCommand(
        string?  payload     = null,
        string?  eventId     = null,
        Guid?    correlationId = null,
        string?  timestamp   = null,
        string?  hmacSig     = null) =>
        new(
            EventId:       eventId       ?? ValidEventId,
            CorrelationId: correlationId ?? Guid.NewGuid(),
            KeyId:         ValidKeyId,
            Timestamp:     timestamp ?? FreshTimestamp,
            Nonce:         ValidNonce,
            HmacSignature: hmacSig  ?? ValidSignature,
            Payload:       payload  ?? ValidPayload,
            SourceIp:      "127.0.0.1");

    /// <summary>Creates a <see cref="RunRecord"/> in <see cref="RunState.HandedOff"/> state.</summary>
    private static RunRecord HandedOffRun(Guid? correlationId = null)
    {
        var id  = Guid.NewGuid();
        var now = FixedNow;

        var run = new RunRecord(
            id:               id,
            workflowId:       "wf-oc07",
            createdAt:        now.AddMinutes(-60),
            correlationId:    correlationId ?? Guid.NewGuid(),
            scriptArtifactId: "artifact-oc07",
            scriptVersion:    "1.0.0",
            schemaVersion:    "1",
            inputRefs:        [],
            secretRefs:       [],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);

        run.Enqueue(now.AddMinutes(-55));
        run.AdoptLease(Guid.NewGuid(), "worker-oc07", now.AddMinutes(-50));
        run.Start(run.AttemptId, now.AddMinutes(-45));
        run.HandOff(run.AttemptId, now.AddMinutes(-30));

        return run;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GROUP 1 — Forbidden payload field checks (step 1 → 400)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AutomationId_InPayload_Returns400()
    {
        // Arrange
        var handler = MakeHandler(
            out _, out _, out _, out _, out _, out _, out _,
            hmacValid: false /* not reached */);
        var cmd = ValidCommand(payload: """{"automationId":"abc","other":"val"}""");

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SERVICE_ROUTING_FORBIDDEN");
        result.Error.Message.Should().Contain("automationId");
    }

    [Fact]
    public async Task ScriptVersionRef_InPayload_Returns400()
    {
        // Arrange
        var handler = MakeHandler(
            out _, out _, out _, out _, out _, out _, out _,
            hmacValid: false);
        var cmd = ValidCommand(payload: """{"scriptVersionRef":"v1.2.3"}""");

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SERVICE_ROUTING_FORBIDDEN");
        result.Error.Message.Should().Contain("scriptVersionRef");
    }

    [Fact]
    public async Task ContinuationScriptRef_InPayload_Returns400()
    {
        // Arrange
        var handler = MakeHandler(
            out _, out _, out _, out _, out _, out _, out _,
            hmacValid: false);
        var cmd = ValidCommand(payload: """{"continuationScriptRef":"some-ref"}""");

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CONTINUATION_SCRIPT_REF_FORBIDDEN");
        result.Error.Message.Should().Contain("continuationScriptRef");
    }

    [Fact]
    public async Task InvalidJson_InPayload_Returns400()
    {
        // Arrange
        var handler = MakeHandler(
            out _, out _, out _, out _, out _, out _, out _,
            hmacValid: false);
        var cmd = ValidCommand(payload: "not-valid-json{{{");

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GROUP 2 — HMAC validation failures (step 2 → 401 INVALID_HMAC)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task InvalidHmac_Returns401_AndEmitsSecurityEvent()
    {
        // Arrange
        var handler = MakeHandler(
            out _,
            out _,
            out _,
            out var securityLog,
            out _,
            out _,
            out _,
            hmacValid: false);
        var cmd = ValidCommand();

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("INVALID_HMAC");

        securityLog.Verify(s => s.AppendAsync(
            "CALLBACK_AUTH_FAILED",
            It.IsAny<string?>(),
            It.Is<string>(d => d.Contains("HMAC")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task InvalidHmac_DoesNotStoreCallbackRecord()
    {
        // Arrange
        var handler = MakeHandler(
            out var callbackRepo,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            hmacValid: false);
        var cmd = ValidCommand();

        // Act
        await handler.HandleAsync(cmd);

        // Assert — AddAsync must never be called when HMAC is invalid.
        callbackRepo.Verify(
            r => r.AddAsync(It.IsAny<CallbackRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GROUP 3 — Replay-window check (step 3 → 401 REPLAY_DETECTED)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StaleTimestamp_MoreThan5MinutesOld_Returns401ReplayDetected()
    {
        // Arrange
        var staleTime = FixedNow.AddMinutes(-6); // 6 minutes in the past → stale
        var clock     = new FakeClock(FixedNow);
        var handler   = MakeHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            clock:     clock,
            hmacValid: true);
        var cmd = ValidCommand(timestamp: staleTime.ToString("O"));

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("REPLAY_DETECTED");
    }

    [Fact]
    public async Task StaleTimestamp_EmitsSecurityEvent()
    {
        // Arrange
        var staleTime = FixedNow.AddMinutes(-10);
        var clock     = new FakeClock(FixedNow);
        var handler   = MakeHandler(
            out _,
            out _,
            out _,
            out var securityLog,
            out _,
            out _,
            out _,
            clock:     clock,
            hmacValid: true);
        var cmd = ValidCommand(timestamp: staleTime.ToString("O"));

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        securityLog.Verify(s => s.AppendAsync(
            "REPLAY_DETECTED",
            It.IsAny<string?>(),
            It.Is<string>(d => d.Contains("replay") || d.Contains("window") || d.Contains("Timestamp")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FutureTimestamp_MoreThan5MinutesAhead_Returns401ReplayDetected()
    {
        // Arrange — future timestamp also outside the ±5 minute window.
        var futureTime = FixedNow.AddMinutes(6);
        var clock      = new FakeClock(FixedNow);
        var handler    = MakeHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            clock:     clock,
            hmacValid: true);
        var cmd = ValidCommand(timestamp: futureTime.ToString("O"));

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("REPLAY_DETECTED");
    }

    [Fact]
    public async Task UnparseableTimestamp_Returns401ReplayDetected()
    {
        // Arrange — HMAC valid, but timestamp is garbage.
        var handler = MakeHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            hmacValid: true);
        var cmd = ValidCommand(timestamp: "not-a-timestamp");

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("REPLAY_DETECTED");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GROUP 4 — Duplicate EventId (step 4 → 200 DUPLICATE)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DuplicateEventId_Returns200Duplicate_WithoutStoringSecondRecord()
    {
        // Arrange
        var existingRecord = new CallbackRecord(
            id:            Guid.NewGuid(),
            eventId:       ValidEventId,
            correlationId: Guid.NewGuid(),
            keyId:         ValidKeyId,
            receivedAt:    FixedNow.AddSeconds(-30),
            payloadJson:   ValidPayload);

        var handler = MakeHandler(
            out var callbackRepo,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            hmacValid:       true,
            existingCallback: existingRecord);
        var cmd = ValidCommand(eventId: ValidEventId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be(IngestCallbackOutcome.Duplicate);
        result.Value.EventId.Should().Be(ValidEventId);

        // No second AddAsync must be triggered.
        callbackRepo.Verify(
            r => r.AddAsync(It.IsAny<CallbackRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DuplicateEventId_DoesNotCommitTransaction()
    {
        // Arrange
        var existingRecord = new CallbackRecord(
            id:            Guid.NewGuid(),
            eventId:       ValidEventId,
            correlationId: Guid.NewGuid(),
            keyId:         ValidKeyId,
            receivedAt:    FixedNow.AddSeconds(-30),
            payloadJson:   ValidPayload);

        var handler = MakeHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out var unitOfWork,
            hmacValid:       true,
            existingCallback: existingRecord);
        var cmd = ValidCommand(eventId: ValidEventId);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — no transaction opened or committed for a duplicate.
        unitOfWork.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()),  Times.Never);
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GROUP 5 — CorrelationId not found (step 5 → 200 CorrelationNotFound)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CorrelationIdNotFound_Returns200CorrelationNotFound()
    {
        // Arrange — run repo returns null (no run for the CorrelationId).
        var handler = MakeHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            hmacValid:   true,
            existingRun: null);
        var cmd = ValidCommand();

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be(IngestCallbackOutcome.CorrelationNotFound);
    }

    [Fact]
    public async Task CorrelationIdNotFound_DoesNotStoreCallbackRecord()
    {
        // Arrange
        var handler = MakeHandler(
            out var callbackRepo,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            hmacValid:   true,
            existingRun: null);
        var cmd = ValidCommand();

        // Act
        await handler.HandleAsync(cmd);

        // Assert — nothing persisted when correlation ID is unknown.
        callbackRepo.Verify(
            r => r.AddAsync(It.IsAny<CallbackRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GROUP 6 — Run not in HANDED_OFF state (step 6 → 422 NOT_HANDED_OFF)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RunInRunningState_Returns422NotHandedOff()
    {
        // Arrange — build a run that is RUNNING (not HANDED_OFF).
        var correlationId = Guid.NewGuid();
        var runningRun    = new RunRecord(
            id:               Guid.NewGuid(),
            workflowId:       "wf-oc07",
            createdAt:        FixedNow.AddMinutes(-30),
            correlationId:    correlationId,
            scriptArtifactId: "artifact-oc07",
            scriptVersion:    "1.0.0",
            schemaVersion:    "1",
            inputRefs:        [],
            secretRefs:       [],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);
        runningRun.Enqueue(FixedNow.AddMinutes(-25));
        runningRun.AdoptLease(Guid.NewGuid(), "worker-oc07", FixedNow.AddMinutes(-20));
        runningRun.Start(runningRun.AttemptId, FixedNow.AddMinutes(-15));
        // intentionally NOT calling HandOff — state is RUNNING

        var handler = MakeHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            hmacValid:   true,
            existingRun: runningRun);
        var cmd = ValidCommand(correlationId: correlationId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_HANDED_OFF");
    }

    [Fact]
    public async Task RunInSucceededState_Returns422NotHandedOff()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var run           = new RunRecord(
            id:               Guid.NewGuid(),
            workflowId:       "wf-oc07",
            createdAt:        FixedNow.AddHours(-2),
            correlationId:    correlationId,
            scriptArtifactId: "artifact-oc07",
            scriptVersion:    "1.0.0",
            schemaVersion:    "1",
            inputRefs:        [],
            secretRefs:       [],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);
        run.Enqueue(FixedNow.AddHours(-2).AddMinutes(5));
        run.AdoptLease(Guid.NewGuid(), "worker-oc07", FixedNow.AddHours(-1).AddMinutes(50));
        run.Start(run.AttemptId, FixedNow.AddHours(-1).AddMinutes(40));
        run.Succeed(run.AttemptId, FixedNow.AddHours(-1));

        var handler = MakeHandler(
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            out _,
            hmacValid:   true,
            existingRun: run);
        var cmd = ValidCommand(correlationId: correlationId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NOT_HANDED_OFF");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GROUP 7 — Happy path (step 7 → 200 Accepted)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidHmacAndHandedOffRun_Returns200Accepted()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var run           = HandedOffRun(correlationId);

        CallbackRecord? capturedRecord = null;

        var callbackRepo      = new Mock<ICallbackRepository>(MockBehavior.Strict);
        var runRepo           = new Mock<IRunRepository>(MockBehavior.Strict);
        var journalRepo       = new Mock<IJournalRepository>(MockBehavior.Strict);
        var securityLog       = new Mock<ISecurityEventLog>(MockBehavior.Strict);
        var hmacValidator     = new Mock<IHmacValidator>(MockBehavior.Strict);
        var continuationQueue = new Mock<IContinuationQueue>(MockBehavior.Strict);
        var unitOfWork        = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var clock             = new FakeClock(FixedNow);

        hmacValidator
            .Setup(h => h.Validate(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        callbackRepo
            .Setup(r => r.FindByEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallbackRecord?)null);

        callbackRepo
            .Setup(r => r.AddAsync(It.IsAny<CallbackRecord>(), It.IsAny<CancellationToken>()))
            .Callback<CallbackRecord, CancellationToken>((rec, _) => capturedRecord = rec)
            .Returns(Task.CompletedTask);

        runRepo
            .Setup(r => r.FindByCorrelationIdAsync(correlationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        continuationQueue
            .Setup(q => q.EnqueueAsync(
                run.Id, correlationId, ValidPayload, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        journalRepo
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync()).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var handler = new IngestCallbackCommandHandler(
            callbackRepo.Object,
            runRepo.Object,
            journalRepo.Object,
            securityLog.Object,
            hmacValidator.Object,
            continuationQueue.Object,
            unitOfWork.Object,
            clock,
            Mock.Of<ITelemetryService>(),
            NullLogger<IngestCallbackCommandHandler>.Instance);

        var cmd = ValidCommand(correlationId: correlationId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be(IngestCallbackOutcome.Accepted);
        result.Value.EventId.Should().Be(ValidEventId);
        result.Value.CorrelationId.Should().Be(correlationId);
    }

    [Fact]
    public async Task ValidHmacAndHandedOffRun_StoresCallbackRecord_WithProcessedTrue()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var run           = HandedOffRun(correlationId);

        CallbackRecord? captured = null;

        var callbackRepo      = new Mock<ICallbackRepository>(MockBehavior.Strict);
        var runRepo           = new Mock<IRunRepository>(MockBehavior.Strict);
        var journalRepo       = new Mock<IJournalRepository>(MockBehavior.Strict);
        var securityLog       = new Mock<ISecurityEventLog>(MockBehavior.Strict);
        var hmacValidator     = new Mock<IHmacValidator>(MockBehavior.Strict);
        var continuationQueue = new Mock<IContinuationQueue>(MockBehavior.Strict);
        var unitOfWork        = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var clock             = new FakeClock(FixedNow);

        hmacValidator
            .Setup(h => h.Validate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        callbackRepo
            .Setup(r => r.FindByEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallbackRecord?)null);

        callbackRepo
            .Setup(r => r.AddAsync(It.IsAny<CallbackRecord>(), It.IsAny<CancellationToken>()))
            .Callback<CallbackRecord, CancellationToken>((rec, _) => captured = rec)
            .Returns(Task.CompletedTask);

        runRepo
            .Setup(r => r.FindByCorrelationIdAsync(correlationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        continuationQueue
            .Setup(q => q.EnqueueAsync(run.Id, correlationId, ValidPayload, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        journalRepo
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync()).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var handler = new IngestCallbackCommandHandler(
            callbackRepo.Object, runRepo.Object, journalRepo.Object,
            securityLog.Object, hmacValidator.Object, continuationQueue.Object,
            unitOfWork.Object, clock,
            Mock.Of<ITelemetryService>(),
            NullLogger<IngestCallbackCommandHandler>.Instance);

        var cmd = ValidCommand(correlationId: correlationId);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — CallbackRecord stored with correct fields and Processed = true.
        captured.Should().NotBeNull();
        captured!.EventId.Should().Be(ValidEventId);
        captured.CorrelationId.Should().Be(correlationId);
        captured.KeyId.Should().Be(ValidKeyId);
        captured.Processed.Should().BeTrue("MarkProcessed must be called before Commit");
    }

    [Fact]
    public async Task ValidHmacAndHandedOffRun_EnqueuesContinuation()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var run           = HandedOffRun(correlationId);

        var callbackRepo      = new Mock<ICallbackRepository>(MockBehavior.Strict);
        var runRepo           = new Mock<IRunRepository>(MockBehavior.Strict);
        var journalRepo       = new Mock<IJournalRepository>(MockBehavior.Strict);
        var securityLog       = new Mock<ISecurityEventLog>(MockBehavior.Strict);
        var hmacValidator     = new Mock<IHmacValidator>(MockBehavior.Strict);
        var continuationQueue = new Mock<IContinuationQueue>(MockBehavior.Strict);
        var unitOfWork        = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var clock             = new FakeClock(FixedNow);

        hmacValidator
            .Setup(h => h.Validate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        callbackRepo
            .Setup(r => r.FindByEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallbackRecord?)null);

        callbackRepo
            .Setup(r => r.AddAsync(It.IsAny<CallbackRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        runRepo
            .Setup(r => r.FindByCorrelationIdAsync(correlationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        continuationQueue
            .Setup(q => q.EnqueueAsync(run.Id, correlationId, ValidPayload, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        journalRepo
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync()).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var handler = new IngestCallbackCommandHandler(
            callbackRepo.Object, runRepo.Object, journalRepo.Object,
            securityLog.Object, hmacValidator.Object, continuationQueue.Object,
            unitOfWork.Object, clock,
            Mock.Of<ITelemetryService>(),
            NullLogger<IngestCallbackCommandHandler>.Instance);

        var cmd = ValidCommand(correlationId: correlationId);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — continuation enqueued exactly once with the correct parameters.
        continuationQueue.Verify(q => q.EnqueueAsync(
            run.Id,
            correlationId,
            ValidPayload,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ValidHmacAndHandedOffRun_CommitsTransaction()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var run           = HandedOffRun(correlationId);

        var callbackRepo      = new Mock<ICallbackRepository>(MockBehavior.Strict);
        var runRepo           = new Mock<IRunRepository>(MockBehavior.Strict);
        var journalRepo       = new Mock<IJournalRepository>(MockBehavior.Strict);
        var securityLog       = new Mock<ISecurityEventLog>(MockBehavior.Strict);
        var hmacValidator     = new Mock<IHmacValidator>(MockBehavior.Strict);
        var continuationQueue = new Mock<IContinuationQueue>(MockBehavior.Strict);
        var unitOfWork        = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var clock             = new FakeClock(FixedNow);

        hmacValidator
            .Setup(h => h.Validate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        callbackRepo
            .Setup(r => r.FindByEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallbackRecord?)null);

        callbackRepo
            .Setup(r => r.AddAsync(It.IsAny<CallbackRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        runRepo
            .Setup(r => r.FindByCorrelationIdAsync(correlationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        continuationQueue
            .Setup(q => q.EnqueueAsync(run.Id, correlationId, ValidPayload, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        journalRepo
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync()).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var handler = new IngestCallbackCommandHandler(
            callbackRepo.Object, runRepo.Object, journalRepo.Object,
            securityLog.Object, hmacValidator.Object, continuationQueue.Object,
            unitOfWork.Object, clock,
            Mock.Of<ITelemetryService>(),
            NullLogger<IngestCallbackCommandHandler>.Instance);

        var cmd = ValidCommand(correlationId: correlationId);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — transaction opened and committed exactly once.
        unitOfWork.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()),  Times.Once);
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        unitOfWork.Verify(u => u.RollbackAsync(),                             Times.Never);
    }

    [Fact]
    public async Task ValidHmacAndHandedOffRun_AppendsJournalEntry()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var run           = HandedOffRun(correlationId);

        var callbackRepo      = new Mock<ICallbackRepository>(MockBehavior.Strict);
        var runRepo           = new Mock<IRunRepository>(MockBehavior.Strict);
        var journalRepo       = new Mock<IJournalRepository>(MockBehavior.Strict);
        var securityLog       = new Mock<ISecurityEventLog>(MockBehavior.Strict);
        var hmacValidator     = new Mock<IHmacValidator>(MockBehavior.Strict);
        var continuationQueue = new Mock<IContinuationQueue>(MockBehavior.Strict);
        var unitOfWork        = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var clock             = new FakeClock(FixedNow);

        hmacValidator
            .Setup(h => h.Validate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        callbackRepo
            .Setup(r => r.FindByEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallbackRecord?)null);

        callbackRepo
            .Setup(r => r.AddAsync(It.IsAny<CallbackRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        runRepo
            .Setup(r => r.FindByCorrelationIdAsync(correlationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        continuationQueue
            .Setup(q => q.EnqueueAsync(run.Id, correlationId, ValidPayload, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        JournalEntry? capturedEntry = null;
        journalRepo
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Callback<JournalEntry, CancellationToken>((e, _) => capturedEntry = e)
            .Returns(Task.CompletedTask);

        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync()).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var handler = new IngestCallbackCommandHandler(
            callbackRepo.Object, runRepo.Object, journalRepo.Object,
            securityLog.Object, hmacValidator.Object, continuationQueue.Object,
            unitOfWork.Object, clock,
            Mock.Of<ITelemetryService>(),
            NullLogger<IngestCallbackCommandHandler>.Instance);

        var cmd = ValidCommand(correlationId: correlationId);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — a CALLBACK_INGESTED journal entry is written for the correct run.
        capturedEntry.Should().NotBeNull();
        capturedEntry!.RunId.Should().Be(run.Id);
        capturedEntry.EventType.Should().Be(JournalEventTypes.CallbackIngested);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GROUP 8 — HMAC step does not invoke security event on success
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ValidRequest_DoesNotEmitSecurityEvent()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var run           = HandedOffRun(correlationId);

        var callbackRepo      = new Mock<ICallbackRepository>(MockBehavior.Strict);
        var runRepo           = new Mock<IRunRepository>(MockBehavior.Strict);
        var journalRepo       = new Mock<IJournalRepository>(MockBehavior.Strict);
        var securityLog       = new Mock<ISecurityEventLog>();   // lenient — Verify Never below
        var hmacValidator     = new Mock<IHmacValidator>(MockBehavior.Strict);
        var continuationQueue = new Mock<IContinuationQueue>(MockBehavior.Strict);
        var unitOfWork        = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var clock             = new FakeClock(FixedNow);

        hmacValidator
            .Setup(h => h.Validate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        callbackRepo
            .Setup(r => r.FindByEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallbackRecord?)null);

        callbackRepo
            .Setup(r => r.AddAsync(It.IsAny<CallbackRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        runRepo
            .Setup(r => r.FindByCorrelationIdAsync(correlationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        continuationQueue
            .Setup(q => q.EnqueueAsync(run.Id, correlationId, ValidPayload, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        journalRepo
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync()).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var handler = new IngestCallbackCommandHandler(
            callbackRepo.Object, runRepo.Object, journalRepo.Object,
            securityLog.Object, hmacValidator.Object, continuationQueue.Object,
            unitOfWork.Object, clock,
            Mock.Of<ITelemetryService>(),
            NullLogger<IngestCallbackCommandHandler>.Instance);

        var cmd = ValidCommand(correlationId: correlationId);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — no security events on the happy path.
        securityLog.Verify(
            s => s.AppendAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(),
                               It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GROUP 9 — Timestamp within window is accepted
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TimestampExactlyAtWindowBoundary_IsAccepted()
    {
        // Arrange — timestamp exactly 5 minutes ago (boundary is inclusive at 5 min).
        var boundaryTime = FixedNow.AddMinutes(-5);
        var clock        = new FakeClock(FixedNow);
        var correlationId = Guid.NewGuid();
        var run           = HandedOffRun(correlationId);

        var callbackRepo      = new Mock<ICallbackRepository>(MockBehavior.Strict);
        var runRepo           = new Mock<IRunRepository>(MockBehavior.Strict);
        var journalRepo       = new Mock<IJournalRepository>(MockBehavior.Strict);
        var securityLog       = new Mock<ISecurityEventLog>(MockBehavior.Strict);
        var hmacValidator     = new Mock<IHmacValidator>(MockBehavior.Strict);
        var continuationQueue = new Mock<IContinuationQueue>(MockBehavior.Strict);
        var unitOfWork        = new Mock<IUnitOfWork>(MockBehavior.Strict);

        hmacValidator
            .Setup(h => h.Validate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

        callbackRepo
            .Setup(r => r.FindByEventIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CallbackRecord?)null);

        callbackRepo
            .Setup(r => r.AddAsync(It.IsAny<CallbackRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        runRepo
            .Setup(r => r.FindByCorrelationIdAsync(correlationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(run);

        continuationQueue
            .Setup(q => q.EnqueueAsync(run.Id, correlationId, ValidPayload, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        journalRepo
            .Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.RollbackAsync()).Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var handler = new IngestCallbackCommandHandler(
            callbackRepo.Object, runRepo.Object, journalRepo.Object,
            securityLog.Object, hmacValidator.Object, continuationQueue.Object,
            unitOfWork.Object, clock,
            Mock.Of<ITelemetryService>(),
            NullLogger<IngestCallbackCommandHandler>.Instance);

        var cmd = ValidCommand(correlationId: correlationId, timestamp: boundaryTime.ToString("O"));

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — exactly at boundary should be accepted (skew == 5 min, not > 5 min).
        result.IsSuccess.Should().BeTrue();
        result.Value.Outcome.Should().Be(IngestCallbackOutcome.Accepted);
    }
}
