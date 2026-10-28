#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Artifacts;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Telemetry;
using Tracto.Orchestrator.Infrastructure.Clock;
using AppClock = Tracto.Orchestrator.Application.Abstractions.IClock;

namespace Tracto.Orchestrator.Unit.Tests.Runs;

/// <summary>
/// Unit tests for <see cref="CreateJobCommandHandler"/> — story OC-02.
/// Each test covers exactly one observable behaviour.
/// FakeClock controls time; Moq stubs all external dependencies.
/// </summary>
public sealed class CreateJobCommandTests
{
    // ── Fixed test baseline ───────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);

    private const string ValidArtifactId  = "script-artifact-001";
    private const string ValidVersion     = "1.0.0";
    private const string ValidSchema      = "1";

    // ── Infrastructure helpers ────────────────────────────────────────────────

    /// <summary>Builds a valid <see cref="ArtifactRecord"/> that the repository mock returns.</summary>
    private static ArtifactRecord ValidArtifact() =>
        new(ValidArtifactId, ValidVersion, ValidSchema,
            "abc123def456", FixedNow.AddDays(-1),
            signatureValid: true, selectorContent: null);

    /// <summary>
    /// Creates a fully wired handler with Moq-backed dependencies.
    /// Out-params expose mocks for fine-grained assertions per test.
    /// The artifact repository defaults to returning a valid, non-deleted artifact.
    /// </summary>
    private static CreateJobCommandHandler MakeHandler(
        out Mock<IRunRepository>      runRepo,
        out Mock<IJournalRepository>  journalRepo,
        out Mock<IArtifactRepository> artifactRepo,
        out Mock<IUnitOfWork>         unitOfWork,
        FakeClock?  clock         = null,
        ArtifactRecord? artifact  = null,
        bool        artifactFound = true)
    {
        runRepo      = new Mock<IRunRepository>(MockBehavior.Strict);
        journalRepo  = new Mock<IJournalRepository>(MockBehavior.Strict);
        artifactRepo = new Mock<IArtifactRepository>(MockBehavior.Strict);
        unitOfWork   = new Mock<IUnitOfWork>(MockBehavior.Strict);

        // Telemetry service — permissive (EmitAsync is a no-op in these tests).
        var telemetry = new Mock<ITelemetryService>();
        telemetry.Setup(t => t.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        // Artifact lookup default: found + not deleted.
        artifactRepo
            .Setup(r => r.GetRecordAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(artifactFound ? (artifact ?? ValidArtifact()) : (ArtifactRecord?)null);

        // UnitOfWork lifecycle.
        unitOfWork.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        unitOfWork.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        // RunRepository write operations.
        runRepo.Setup(r => r.AddAsync(It.IsAny<RunRecord>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        runRepo.Setup(r => r.Update(It.IsAny<RunRecord>()));

        // EventId dedup: no existing run by default.
        runRepo.Setup(r => r.FindByEventIdAsync(
                   It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((RunRecord?)null);

        // Journal append never fails.
        journalRepo.Setup(j => j.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
                   .Returns(Task.CompletedTask);

        return new CreateJobCommandHandler(
            runRepo.Object,
            artifactRepo.Object,
            journalRepo.Object,
            unitOfWork.Object,
            clock ?? new FakeClock(FixedNow),
            telemetry.Object,
            NullLogger<CreateJobCommandHandler>.Instance);
    }

    /// <summary>Returns a valid command with sensible defaults.</summary>
    private static CreateJobCommand ValidCommand(
        string?  continuationScriptRef = null,
        string   schemaVersion         = ValidSchema,
        string   artifactId            = ValidArtifactId,
        string   version               = ValidVersion,
        DateTime? deadline             = null,
        string?  eventId               = null,
        TriggerType triggerType        = TriggerType.Manual,
        string?  businessJobId         = null) =>
        new(ScriptArtifactId:     artifactId,
            ScriptVersion:        version,
            SchemaVersion:        schemaVersion,
            InputRefs:            [],
            SecretRefs:           [],
            Deadline:             deadline,
            EventId:              eventId,
            TriggerType:          triggerType,
            ContinuationScriptRef: continuationScriptRef,
            BusinessJobId:        businessJobId);

    // ── 1. Happy path: new job → QUEUED ──────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ValidCommand_ReturnsSuccessWithRunIdAndQueuedState()
    {
        // Arrange
        var handler = MakeHandler(
            out var runRepo, out _, out _, out _);

        RunRecord? captured = null;
        runRepo.Setup(r => r.AddAsync(It.IsAny<RunRecord>(), It.IsAny<CancellationToken>()))
               .Callback<RunRecord, CancellationToken>((r, _) => captured = r)
               .Returns(Task.CompletedTask);

        // Act
        var result = await handler.HandleAsync(ValidCommand());

        // Assert
        result.IsSuccess.Should().BeTrue(because: "a valid command must succeed");
        result.Value.RunId.Should().NotBeEmpty(because: "a RunId must be assigned");
        result.Value.State.Should().Be("QUEUED", because: "the run must be immediately queued");

        captured.Should().NotBeNull(because: "AddAsync must have been called with the new run");
        captured!.ScriptArtifactId.Should().Be(ValidArtifactId);
        captured.ScriptVersion.Should().Be(ValidVersion);
        captured.SchemaVersion.Should().Be(ValidSchema);
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_StateIsQueuedAfterCommit()
    {
        // Arrange
        var handler = MakeHandler(
            out var runRepo, out _, out _, out _);

        RunRecord? captured = null;
        runRepo.Setup(r => r.AddAsync(It.IsAny<RunRecord>(), It.IsAny<CancellationToken>()))
               .Callback<RunRecord, CancellationToken>((r, _) => captured = r)
               .Returns(Task.CompletedTask);

        // Act
        await handler.HandleAsync(ValidCommand());

        // Assert — after the handler runs, Enqueue() must have been called on the run.
        captured.Should().NotBeNull();
        captured!.State.Should().Be(RunState.Queued,
            because: "the handler must transition the run from CREATED to QUEUED before committing");
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_TwoJournalEntriesEmitted()
    {
        // Arrange
        var handler = MakeHandler(
            out _, out var journalRepo, out _, out _);

        // Act
        await handler.HandleAsync(ValidCommand());

        // Assert — RUN_CREATED and RUN_QUEUED must each be appended once.
        journalRepo.Verify(
            j => j.AppendAsync(
                It.Is<JournalEntry>(e => e.EventType == "RUN_CREATED"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a RUN_CREATED journal entry must be written");

        journalRepo.Verify(
            j => j.AppendAsync(
                It.Is<JournalEntry>(e => e.EventType == "RUN_QUEUED"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a RUN_QUEUED journal entry must be written");
    }

    [Fact]
    public async Task HandleAsync_ValidCommand_CommitsUnitOfWork()
    {
        // Arrange
        var handler = MakeHandler(
            out _, out _, out _, out var uow);

        // Act
        await handler.HandleAsync(ValidCommand());

        // Assert
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once,
            "CommitAsync must be called exactly once to satisfy invariant 5");
    }

    // ── 2. ContinuationScriptRef present → 400 CONTINUATION_SCRIPT_REF_FORBIDDEN

    [Fact]
    public async Task HandleAsync_ContinuationScriptRefProvided_ReturnsError400()
    {
        // Arrange
        var handler = MakeHandler(out _, out _, out _, out _);

        var cmd = ValidCommand(continuationScriptRef: "some-continuation-script-ref");

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "ContinuationScriptRef is forbidden on manual job creation");
        result.Error.Code.Should().Be("CONTINUATION_SCRIPT_REF_FORBIDDEN");
    }

    [Theory]
    [InlineData("any-ref")]
    [InlineData("continuation://script/v1")]
    [InlineData("  ref  ")]
    public async Task HandleAsync_AnyContinuationScriptRef_IsForbidden(string continuationRef)
    {
        // Arrange
        var handler = MakeHandler(out _, out _, out _, out _);
        var cmd = ValidCommand(continuationScriptRef: continuationRef);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("CONTINUATION_SCRIPT_REF_FORBIDDEN");
    }

    [Fact]
    public async Task HandleAsync_ContinuationScriptRef_NoWritesMade()
    {
        // Arrange — use loose mocks so no setup is needed for write operations.
        var runRepo      = new Mock<IRunRepository>();
        var journalRepo  = new Mock<IJournalRepository>();
        var artifactRepo = new Mock<IArtifactRepository>();
        var unitOfWork   = new Mock<IUnitOfWork>();

        var looseTelemetry = new Mock<ITelemetryService>();
        looseTelemetry.Setup(t => t.EmitAsync(It.IsAny<TelemetryEvent>(), It.IsAny<CancellationToken>()))
                      .Returns(Task.CompletedTask);
        var handler = new CreateJobCommandHandler(
            runRepo.Object, artifactRepo.Object, journalRepo.Object,
            unitOfWork.Object, new FakeClock(FixedNow),
            looseTelemetry.Object,
            NullLogger<CreateJobCommandHandler>.Instance);

        var cmd = ValidCommand(continuationScriptRef: "ref");

        // Act
        await handler.HandleAsync(cmd);

        // Assert — nothing must have been persisted.
        runRepo.Verify(r => r.AddAsync(It.IsAny<RunRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        unitOfWork.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 3. SchemaVersion != "1" → 422 UNSUPPORTED_SCHEMA ─────────────────────

    [Theory]
    [InlineData("2")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("latest")]
    public async Task HandleAsync_UnsupportedSchemaVersion_ReturnsUnsupportedSchemaError(string badSchema)
    {
        // Arrange
        var handler = MakeHandler(out _, out _, out _, out _);
        var cmd = ValidCommand(schemaVersion: badSchema);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: $"schema version '{badSchema}' is not supported");
        result.Error.Code.Should().Be("UNSUPPORTED_SCHEMA",
            because: "the rejection code must be UNSUPPORTED_SCHEMA for an unsupported schema");
    }

    [Fact]
    public async Task HandleAsync_SchemaV1_Succeeds()
    {
        // Arrange
        var handler = MakeHandler(out _, out _, out _, out _);

        // Act
        var result = await handler.HandleAsync(ValidCommand(schemaVersion: "1"));

        // Assert
        result.IsSuccess.Should().BeTrue(because: "schema version '1' is the supported version");
    }

    // ── 4. Artifact not found → 422 ARTIFACT_NOT_FOUND ───────────────────────

    [Fact]
    public async Task HandleAsync_ArtifactNotFound_ReturnsArtifactNotFoundError()
    {
        // Arrange
        var handler = MakeHandler(out _, out _, out _, out _,
            artifactFound: false);   // repository returns null

        var cmd = ValidCommand();

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "an unknown artifact must be rejected before creating a run");
        result.Error.Code.Should().Be("ARTIFACT_NOT_FOUND");
    }

    [Fact]
    public async Task HandleAsync_ArtifactSoftDeleted_ReturnsArtifactNotFoundError()
    {
        // Arrange — artifact exists but is soft-deleted.
        var deletedArtifact = ValidArtifact();
        deletedArtifact.SoftDelete();

        var handler = MakeHandler(out _, out _, out _, out _,
            artifact: deletedArtifact, artifactFound: true);

        // Act
        var result = await handler.HandleAsync(ValidCommand());

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "a soft-deleted artifact must be treated as not found");
        result.Error.Code.Should().Be("ARTIFACT_NOT_FOUND");
    }

    // ── 5. Deadline in past → 422 DEADLINE_IN_PAST ───────────────────────────

    [Fact]
    public async Task HandleAsync_DeadlineInPast_ReturnsDeadlineInPastError()
    {
        // Arrange
        var clock   = new FakeClock(FixedNow);
        var handler = MakeHandler(out _, out _, out _, out _, clock: clock);

        // Deadline one second before FixedNow.
        var pastDeadline = FixedNow.UtcDateTime.AddSeconds(-1);
        var cmd = ValidCommand(deadline: pastDeadline);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "a deadline that has already passed must be rejected");
        result.Error.Code.Should().Be("DEADLINE_IN_PAST");
    }

    [Fact]
    public async Task HandleAsync_DeadlineExactlyNow_IsRejected()
    {
        // Arrange — deadline == now (not strictly in the future).
        var clock   = new FakeClock(FixedNow);
        var handler = MakeHandler(out _, out _, out _, out _, clock: clock);

        var deadlineNow = FixedNow.UtcDateTime;   // equal to UtcNow → not in the future (uses <=)
        var cmd = ValidCommand(deadline: deadlineNow);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — the handler uses <= comparison: deadline == now is not in the future, reject it.
        result.IsFailure.Should().BeTrue(
            because: "a deadline exactly equal to now is not in the future and must be rejected");
        result.Error.Code.Should().Be("DEADLINE_IN_PAST");
    }

    [Fact]
    public async Task HandleAsync_DeadlineInFuture_Succeeds()
    {
        // Arrange
        var clock   = new FakeClock(FixedNow);
        var handler = MakeHandler(out _, out _, out _, out _, clock: clock);

        var futureDeadline = FixedNow.UtcDateTime.AddHours(1);
        var cmd = ValidCommand(deadline: futureDeadline);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "a deadline one hour in the future must be accepted");
    }

    [Fact]
    public async Task HandleAsync_NullDeadline_Succeeds()
    {
        // Arrange
        var handler = MakeHandler(out _, out _, out _, out _);

        // Act
        var result = await handler.HandleAsync(ValidCommand(deadline: null));

        // Assert
        result.IsSuccess.Should().BeTrue(because: "absence of a deadline is valid");
    }

    // ── 6. Duplicate EventId within 5-minute window → idempotent 200 ─────────

    [Fact]
    public async Task HandleAsync_DuplicateEventIdWithinDedupWindow_ReturnsExistingRunId()
    {
        // Arrange
        var clock = new FakeClock(FixedNow);

        var existingRunId = Guid.NewGuid();
        var existingRun   = new RunRecord(
            id:               existingRunId,
            workflowId:       ValidArtifactId,
            createdAt:        FixedNow.AddMinutes(-2),  // 2 min ago — inside 5-min window
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: ValidArtifactId,
            scriptVersion:    ValidVersion,
            schemaVersion:    ValidSchema,
            inputRefs:        [],
            secretRefs:       [],
            deadline:         null,
            eventId:          "evt-dedup-001",
            triggerType:      TriggerType.Callback,
            businessJobId:    null);

        // Simulate CREATED→QUEUED so State is QUEUED.
        existingRun.Enqueue(FixedNow.AddMinutes(-2));

        var handler = MakeHandler(out var runRepo, out _, out _, out _, clock: clock);

        runRepo.Setup(r => r.FindByEventIdAsync(
                   "evt-dedup-001", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(existingRun);

        var cmd = ValidCommand(eventId: "evt-dedup-001", triggerType: TriggerType.Callback);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — must return the original run, not a new one.
        result.IsSuccess.Should().BeTrue(
            because: "a duplicate EventId within the dedup window must be handled idempotently");
        result.Value.RunId.Should().Be(existingRunId,
            because: "the existing RunId must be returned, not a new one");
        result.Value.State.Should().Be("QUEUED",
            because: "the state of the existing run must be reflected in the response");
    }

    [Fact]
    public async Task HandleAsync_DuplicateEventId_NoNewRunCreated()
    {
        // Arrange
        var existingRun = new RunRecord(
            id:               Guid.NewGuid(),
            workflowId:       ValidArtifactId,
            createdAt:        FixedNow.AddMinutes(-1),
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: ValidArtifactId,
            scriptVersion:    ValidVersion,
            schemaVersion:    ValidSchema,
            inputRefs:        [],
            secretRefs:       [],
            deadline:         null,
            eventId:          "evt-no-new-run",
            triggerType:      TriggerType.Callback,
            businessJobId:    null);

        var handler = MakeHandler(out var runRepo, out var journalRepo, out _, out var uow);

        runRepo.Setup(r => r.FindByEventIdAsync(
                   "evt-no-new-run", It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(existingRun);

        var cmd = ValidCommand(eventId: "evt-no-new-run", triggerType: TriggerType.Callback);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — no new run must be persisted.
        runRepo.Verify(r => r.AddAsync(It.IsAny<RunRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "AddAsync must not be called when an existing run is returned for the same EventId");
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()),
            Times.Never,
            "CommitAsync must not be called for an idempotent EventId response");
    }

    [Fact]
    public async Task HandleAsync_EventIdOutsideWindow_CreatesNewRun()
    {
        // Arrange — FindByEventIdAsync returns null (no match within 5-min window).
        var handler = MakeHandler(out var runRepo, out _, out _, out _);

        // FindByEventIdAsync already set to null in MakeHandler defaults.
        var cmd = ValidCommand(eventId: "evt-outside-window", triggerType: TriggerType.Callback);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — a brand-new run must be created.
        result.IsSuccess.Should().BeTrue(because: "EventId outside the dedup window must create a new run");
        runRepo.Verify(r => r.AddAsync(It.IsAny<RunRecord>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "AddAsync must be called once to persist the new run");
    }

    [Fact]
    public async Task HandleAsync_EventIdDedupWindow_IsExactlyFiveMinutes()
    {
        // Arrange — verify the dedup window constant.
        CreateJobCommandHandler.EventIdDedupWindow
            .Should().Be(TimeSpan.FromMinutes(5),
                because: "the dedup window is defined in the spec as exactly 5 minutes");
    }

    // ── 7. TriggerType is stored on the run ───────────────────────────────────

    [Theory]
    [InlineData(TriggerType.Manual)]
    [InlineData(TriggerType.Scheduled)]
    [InlineData(TriggerType.Callback)]
    public async Task HandleAsync_TriggerType_IsStoredOnRun(TriggerType triggerType)
    {
        // Arrange
        var handler = MakeHandler(out var runRepo, out _, out _, out _);

        RunRecord? captured = null;
        runRepo.Setup(r => r.AddAsync(It.IsAny<RunRecord>(), It.IsAny<CancellationToken>()))
               .Callback<RunRecord, CancellationToken>((r, _) => captured = r)
               .Returns(Task.CompletedTask);

        var cmd = ValidCommand(triggerType: triggerType);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        captured.Should().NotBeNull();
        captured!.TriggerType.Should().Be(triggerType,
            because: "the trigger type from the command must be propagated to the run record");
    }

    // ── 8. InputRefs and SecretRefs stored; no secret values ever stored ──────

    [Fact]
    public async Task HandleAsync_InputAndSecretRefs_StoredOnRun()
    {
        // Arrange
        var handler = MakeHandler(out var runRepo, out _, out _, out _);

        RunRecord? captured = null;
        runRepo.Setup(r => r.AddAsync(It.IsAny<RunRecord>(), It.IsAny<CancellationToken>()))
               .Callback<RunRecord, CancellationToken>((r, _) => captured = r)
               .Returns(Task.CompletedTask);

        var cmd = new CreateJobCommand(
            ScriptArtifactId:      ValidArtifactId,
            ScriptVersion:         ValidVersion,
            SchemaVersion:         ValidSchema,
            InputRefs:             ["input://blob/1", "input://blob/2"],
            SecretRefs:            ["secret://vault/key-a"],
            Deadline:              null,
            EventId:               null,
            TriggerType:           TriggerType.Manual,
            ContinuationScriptRef: null,
            BusinessJobId:         null);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — refs stored as references; no actual secret values.
        captured.Should().NotBeNull();
        captured!.InputRefs.Should().BeEquivalentTo(["input://blob/1", "input://blob/2"],
            because: "InputRefs must be stored verbatim (invariant 4 — no plaintext secrets)");
        captured.SecretRefs.Should().BeEquivalentTo(["secret://vault/key-a"],
            because: "SecretRefs stores references, not plaintext secret values (invariant 4)");
    }

    // ── 9. BusinessJobId round-trips through the run record ───────────────────

    [Fact]
    public async Task HandleAsync_BusinessJobId_StoredOnRun()
    {
        // Arrange
        var handler = MakeHandler(out var runRepo, out _, out _, out _);

        RunRecord? captured = null;
        runRepo.Setup(r => r.AddAsync(It.IsAny<RunRecord>(), It.IsAny<CancellationToken>()))
               .Callback<RunRecord, CancellationToken>((r, _) => captured = r)
               .Returns(Task.CompletedTask);

        var cmd = ValidCommand(businessJobId: "biz-job-XYZ");

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        captured.Should().NotBeNull();
        captured!.BusinessJobId.Should().Be("biz-job-XYZ",
            because: "BusinessJobId must be stored on the run for external system correlation");
    }

    // ── 10. ScheduleRecord entity basic invariants ────────────────────────────

    [Fact]
    public void ScheduleRecord_ValidConstruction_SetsAllProperties()
    {
        // Arrange / Act
        var schedule = new ScheduleRecord(
            scheduleId:     Guid.NewGuid(),
            cronExpression: "0 */6 * * *",
            artifactId:     "art-001",
            version:        "2.0.0",
            enabled:        true,
            createdAt:      FixedNow);

        // Assert
        schedule.CronExpression.Should().Be("0 */6 * * *");
        schedule.ArtifactId.Should().Be("art-001");
        schedule.Version.Should().Be("2.0.0");
        schedule.Enabled.Should().BeTrue();
        schedule.CreatedAt.Should().Be(FixedNow);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ScheduleRecord_BlankCronExpression_ThrowsArgumentException(string badCron)
    {
        // Arrange / Act
        var act = () => new ScheduleRecord(
            Guid.NewGuid(), badCron, "art-001", "1.0.0", true, FixedNow);

        // Assert
        act.Should().Throw<ArgumentException>(
            because: "a schedule without a valid cron expression is meaningless");
    }

    [Fact]
    public void ScheduleRecord_Disable_SetsEnabledFalse()
    {
        // Arrange
        var schedule = new ScheduleRecord(
            Guid.NewGuid(), "* * * * *", "art", "1.0.0", true, FixedNow);

        // Act
        schedule.Disable(FixedNow.AddMinutes(1));

        // Assert
        schedule.Enabled.Should().BeFalse(because: "Disable must deactivate the schedule");
        schedule.UpdatedAt.Should().Be(FixedNow.AddMinutes(1));
    }

    [Fact]
    public void ScheduleRecord_Enable_SetsEnabledTrue()
    {
        // Arrange
        var schedule = new ScheduleRecord(
            Guid.NewGuid(), "* * * * *", "art", "1.0.0", false, FixedNow);

        // Act
        schedule.Enable(FixedNow.AddMinutes(5));

        // Assert
        schedule.Enabled.Should().BeTrue(because: "Enable must re-activate a disabled schedule");
        schedule.UpdatedAt.Should().Be(FixedNow.AddMinutes(5));
    }
}
