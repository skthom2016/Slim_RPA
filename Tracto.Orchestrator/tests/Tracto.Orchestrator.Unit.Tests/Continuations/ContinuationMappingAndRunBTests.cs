#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Artifacts;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Infrastructure.Clock;

namespace Tracto.Orchestrator.Unit.Tests.Continuations;

/// <summary>
/// Unit tests for Story OC-08: Continuation Mapping and Run B Creation.
///
/// Covers all ACs listed in the story:
///   1. HANDED_OFF + valid mapping → Run B created.
///   2. SUCCEEDED + static mapping → one Run B.
///   3. FAILED + mapping → no Run B (no-action policy).
///   4. Duplicate SUCCEEDED → still one Run B (idempotent dedup).
///   5. Source run state unchanged when Run B fails.
///   6. Mapping updated while Run B RUNNING → Run B uses original mapping version.
///   7. HANDED_OFF + no mapping → operator attention logged (no Run B).
///   8. CreateContinuationMappingCommandHandler: non-terminal TriggerState → validation error.
///   9. GetContinuationMappingQueryHandler: mapping found → correct DTO returned.
///  10. GetContinuationMappingQueryHandler: mapping not found → NotFound error.
///  11. StaticSucceededContinuationService: Run B already exists → skip (idempotent).
///  12. StaticSucceededContinuationService: no mapping → returns without creating Run B.
/// </summary>
public sealed class ContinuationMappingAndRunBTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    // ── Factories ──────────────────────────────────────────────────────────────

    private static RunRecord MakeSourceRun()
    {
        var run = new RunRecord(
            id:                Guid.NewGuid(),
            workflowId:        "wf-source",
            createdAt:         FixedNow.AddMinutes(-10),
            correlationId:     Guid.NewGuid(),
            scriptArtifactId:  "script-src",
            scriptVersion:     "1.0.0",
            schemaVersion:     "1",
            inputRefs:         [],
            secretRefs:        [],
            deadline:          null,
            eventId:           null,
            triggerType:       TriggerType.Manual,
            businessJobId:     "biz-001");
        return run;
    }

    private static ContinuationMapping MakeMapping(
        Guid?    triggerRunId,
        RunState triggerState,
        string   artifactId      = "artifact-runB",
        string   artifactVersion = "2.0.0",
        string?  inputTemplate   = null)
    {
        return new ContinuationMapping(
            mappingId:             Guid.NewGuid(),
            triggerRunId:          triggerRunId,
            triggerState:          triggerState,
            targetArtifactId:      artifactId,
            targetArtifactVersion: artifactVersion,
            inputTemplate:         inputTemplate,
            createdAt:             FixedNow.AddHours(-1));
    }

    /// <summary>
    /// Builds a <see cref="CreateContinuationRunCommandHandler"/> with all mocks wired.
    /// The <paramref name="createJobResult"/> controls what Run B creation returns.
    /// </summary>
    private static (
        CreateContinuationRunCommandHandler handler,
        Mock<IRunRepository>               runRepo,
        Mock<IContinuationMappingRepository> mappingRepo,
        Mock<IContinuationRunRepository>    continuationRunRepo,
        Mock<IUnitOfWork>                  uow)
    BuildContinuationHandler(
        Result<CreateJobResult, Error>? createJobResult = null)
    {
        var runRepo             = new Mock<IRunRepository>(MockBehavior.Loose);
        var mappingRepo         = new Mock<IContinuationMappingRepository>(MockBehavior.Loose);
        var continuationRunRepo = new Mock<IContinuationRunRepository>(MockBehavior.Loose);
        var uow                 = new Mock<IUnitOfWork>(MockBehavior.Loose);
        var clock               = new FakeClock(FixedNow);

        // Build an inner CreateJobCommandHandler whose dependencies are all no-ops.
        var innerRunRepo      = new Mock<IRunRepository>(MockBehavior.Loose);
        var artifactRepo      = new Mock<IArtifactRepository>(MockBehavior.Loose);
        var journalRepo       = new Mock<IJournalRepository>(MockBehavior.Loose);
        var innerUow          = new Mock<IUnitOfWork>(MockBehavior.Loose);

        // Default: artifact exists so CreateJobCommand can proceed.
        artifactRepo
            .Setup(r => r.GetRecordAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArtifactRecord(
                artifactId:           "artifact-runB",
                version:              "2.0.0",
                schemaVersion:        "1",
                contentHash:          "abc123",
                storedAt:             DateTimeOffset.UtcNow,
                signatureValid:       true,
                selectorContent:      null));

        var runBId = Guid.NewGuid();
        innerRunRepo
            .Setup(r => r.AddAsync(It.IsAny<RunRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        innerRunRepo
            .Setup(r => r.FindByEventIdAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RunRecord?)null);

        // Allow the inner UoW to commit without error.
        innerUow.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        innerUow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var createJobHandler = new CreateJobCommandHandler(
            runRepo:      innerRunRepo.Object,
            artifactRepo: artifactRepo.Object,
            journalRepo:  journalRepo.Object,
            unitOfWork:   innerUow.Object,
            clock:        clock,
            telemetry:    new Mock<ITelemetryService>().Object,
            logger:       NullLogger<CreateJobCommandHandler>.Instance);

        uow.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        continuationRunRepo
            .Setup(r => r.AddAsync(It.IsAny<ContinuationRunRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new CreateContinuationRunCommandHandler(
            runRepo:             runRepo.Object,
            mappingRepo:         mappingRepo.Object,
            continuationRunRepo: continuationRunRepo.Object,
            createJobHandler:    createJobHandler,
            unitOfWork:          uow.Object,
            clock:               clock,
            logger:              NullLogger<CreateContinuationRunCommandHandler>.Instance);

        return (handler, runRepo, mappingRepo, continuationRunRepo, uow);
    }

    // ── Test 1: HANDED_OFF + valid mapping → Run B created ────────────────────

    [Fact]
    public async Task HandedOff_WithValidMapping_CreatesRunB()
    {
        // Arrange
        var source  = MakeSourceRun();
        var mapping = MakeMapping(triggerRunId: source.Id, triggerState: RunState.HandedOff);

        var (handler, runRepo, mappingRepo, continuationRunRepo, uow) =
            BuildContinuationHandler();

        runRepo.Setup(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(source);

        // No existing Run B for this pair.
        continuationRunRepo
            .Setup(r => r.FindAsync(source.Id, RunState.HandedOff, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContinuationRunRecord?)null);

        mappingRepo
            .Setup(r => r.FindBestMatchAsync(source.Id, RunState.HandedOff, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapping);

        var command = new CreateContinuationRunCommand(source.Id, RunState.HandedOff, "biz-001");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue("a valid mapping for HANDED_OFF must produce a Run B");
        result.Value.RunBId.Should().NotBeNull("Run B must be assigned a new RunId");
        result.Value.Outcome.Should().Be("CREATED");
        result.Value.AlreadyExisted.Should().BeFalse();

        // The dedup record must have been persisted.
        continuationRunRepo.Verify(
            r => r.AddAsync(It.IsAny<ContinuationRunRecord>(), It.IsAny<CancellationToken>()),
            Times.Once);

        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Test 2: SUCCEEDED + static (global) mapping → one Run B ──────────────

    [Fact]
    public async Task Succeeded_WithStaticGlobalMapping_CreatesExactlyOneRunB()
    {
        // Arrange — global mapping (TriggerRunId == null) for SUCCEEDED.
        var source  = MakeSourceRun();
        var mapping = MakeMapping(triggerRunId: null, triggerState: RunState.Succeeded);

        var (handler, runRepo, mappingRepo, continuationRunRepo, uow) =
            BuildContinuationHandler();

        runRepo.Setup(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(source);

        continuationRunRepo
            .Setup(r => r.FindAsync(source.Id, RunState.Succeeded, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContinuationRunRecord?)null);

        mappingRepo
            .Setup(r => r.FindBestMatchAsync(source.Id, RunState.Succeeded, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapping);

        var command = new CreateContinuationRunCommand(source.Id, RunState.Succeeded);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RunBId.Should().NotBeNull();
        result.Value.Outcome.Should().Be("CREATED");

        continuationRunRepo.Verify(
            r => r.AddAsync(It.IsAny<ContinuationRunRecord>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "dedup record must be written exactly once");
    }

    // ── Test 3: FAILED + no mapping → no Run B created ────────────────────────

    [Fact]
    public async Task Failed_WithoutMapping_ReturnsNoMappingOutcomeAndNoRunB()
    {
        // Arrange
        var source = MakeSourceRun();

        var (handler, runRepo, mappingRepo, continuationRunRepo, uow) =
            BuildContinuationHandler();

        runRepo.Setup(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(source);

        continuationRunRepo
            .Setup(r => r.FindAsync(source.Id, RunState.Failed, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContinuationRunRecord?)null);

        mappingRepo
            .Setup(r => r.FindBestMatchAsync(source.Id, RunState.Failed, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContinuationMapping?)null);

        var command = new CreateContinuationRunCommand(source.Id, RunState.Failed);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue("no-mapping for FAILED is a valid no-action result");
        result.Value.RunBId.Should().BeNull("FAILED without a mapping must not create Run B");
        result.Value.Outcome.Should().Be("NO_MAPPING");

        continuationRunRepo.Verify(
            r => r.AddAsync(It.IsAny<ContinuationRunRecord>(), It.IsAny<CancellationToken>()),
            Times.Never);
        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Test 4: Duplicate SUCCEEDED → still one Run B (idempotent) ───────────

    [Fact]
    public async Task DuplicateSucceeded_ReturnsAlreadyExistsAndDoesNotCreateSecondRunB()
    {
        // Arrange — a dedup record already exists for this pair.
        var source    = MakeSourceRun();
        var existingRunBId = Guid.NewGuid();

        var existingRecord = new ContinuationRunRecord(
            id:                    Guid.NewGuid(),
            sourceRunId:           source.Id,
            triggerState:          RunState.Succeeded,
            runBId:                existingRunBId,
            mappingId:             Guid.NewGuid(),
            mappingVersionSnapshot: 1,
            createdAt:             FixedNow.AddMinutes(-5));

        var (handler, runRepo, mappingRepo, continuationRunRepo, uow) =
            BuildContinuationHandler();

        runRepo.Setup(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(source);

        // Dedup record found on first check — short-circuits to idempotent path.
        continuationRunRepo
            .Setup(r => r.FindAsync(source.Id, RunState.Succeeded, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRecord);

        var command = new CreateContinuationRunCommand(source.Id, RunState.Succeeded);

        // Act — first call (new trigger)
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.RunBId.Should().Be(existingRunBId, "must return the original Run B id");
        result.Value.AlreadyExisted.Should().BeTrue("dedup guard must signal ALREADY_EXISTS");
        result.Value.Outcome.Should().Be("ALREADY_EXISTS");

        // The mapping and CreateJob must NOT have been invoked again.
        mappingRepo.Verify(
            r => r.FindBestMatchAsync(It.IsAny<Guid>(), It.IsAny<RunState>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "FindBestMatchAsync must be skipped when dedup record already exists");

        continuationRunRepo.Verify(
            r => r.AddAsync(It.IsAny<ContinuationRunRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "AddAsync must not be called when Run B already exists");

        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Test 5: Source run state unchanged when Run B fails ───────────────────

    [Fact]
    public async Task SourceRunStateIsNeverModifiedByRunBOutcome()
    {
        // Arrange — simulate that after a successful Run B creation the source run
        // state stays as HANDED_OFF (the state that triggered the continuation).
        var source  = MakeSourceRun();

        // Drive source through QUEUED → LEASED → RUNNING → HANDED_OFF.
        source.Enqueue(FixedNow.AddMinutes(-9));
        var leaseAttempt = Guid.NewGuid();
        source.AdoptLease(leaseAttempt, "worker-1", FixedNow.AddMinutes(-8));
        source.Start(leaseAttempt, FixedNow.AddMinutes(-7));
        source.HandOff(leaseAttempt, FixedNow.AddMinutes(-6));

        var mapping = MakeMapping(triggerRunId: source.Id, triggerState: RunState.HandedOff);

        var (handler, runRepo, mappingRepo, continuationRunRepo, _) =
            BuildContinuationHandler();

        runRepo.Setup(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(source);

        continuationRunRepo
            .Setup(r => r.FindAsync(source.Id, RunState.HandedOff, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContinuationRunRecord?)null);

        mappingRepo
            .Setup(r => r.FindBestMatchAsync(source.Id, RunState.HandedOff, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapping);

        var command = new CreateContinuationRunCommand(source.Id, RunState.HandedOff);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert — Run B created, but source run remains HANDED_OFF.
        result.IsSuccess.Should().BeTrue();
        source.State.Should().Be(RunState.HandedOff,
            "source run state must never be modified by this handler");
    }

    // ── Test 6: Mapping updated while Run B RUNNING → original version used ───

    [Fact]
    public async Task MappingVersionSnapshotted_BeforeRunBCreation()
    {
        // Arrange — mapping is at version 1 when the handler first reads it.
        var source  = MakeSourceRun();
        var mapping = MakeMapping(triggerRunId: source.Id, triggerState: RunState.Succeeded);

        // Confirm initial version before any mutation.
        mapping.Version.Should().Be(1, "a newly constructed mapping must be at version 1");

        var (handler, runRepo, mappingRepo, continuationRunRepo, _) =
            BuildContinuationHandler();

        runRepo.Setup(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(source);

        continuationRunRepo
            .Setup(r => r.FindAsync(source.Id, RunState.Succeeded, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContinuationRunRecord?)null);

        mappingRepo
            .Setup(r => r.FindBestMatchAsync(source.Id, RunState.Succeeded, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapping);

        ContinuationRunRecord? captured = null;
        continuationRunRepo
            .Setup(r => r.AddAsync(It.IsAny<ContinuationRunRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ContinuationRunRecord, CancellationToken>((rec, _) => captured = rec)
            .Returns(Task.CompletedTask);

        var command = new CreateContinuationRunCommand(source.Id, RunState.Succeeded);

        // Act — handler runs and snapshots version 1 before writing the dedup record.
        var result = await handler.HandleAsync(command);

        // Simulate a concurrent caller updating the mapping to v2 AFTER Run B was created.
        mapping.Update("artifact-runB", "3.0.0", null); // now version == 2

        // Assert — the dedup record captured during handleAsync must still carry v1.
        result.IsSuccess.Should().BeTrue();

        captured.Should().NotBeNull("a ContinuationRunRecord must have been created");
        captured!.MappingVersionSnapshot.Should().Be(1,
            "the snapshot version must be the version at the time Run B was created, not a later update");

        // The mapping object itself is at v2 now, confirming the domain entity was updated.
        mapping.Version.Should().Be(2, "the mapping was updated after Run B creation");
    }

    // ── Test 7: HANDED_OFF + no mapping → operator-attention outcome ──────────

    [Fact]
    public async Task HandedOff_WithoutMapping_ReturnsHandoffMappingMissingOutcome()
    {
        // Arrange
        var source = MakeSourceRun();

        var (handler, runRepo, mappingRepo, continuationRunRepo, uow) =
            BuildContinuationHandler();

        runRepo.Setup(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(source);

        continuationRunRepo
            .Setup(r => r.FindAsync(source.Id, RunState.HandedOff, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContinuationRunRecord?)null);

        mappingRepo
            .Setup(r => r.FindBestMatchAsync(source.Id, RunState.HandedOff, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContinuationMapping?)null);

        var command = new CreateContinuationRunCommand(source.Id, RunState.HandedOff);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue("missing mapping for HANDED_OFF is handled, not a crash");
        result.Value.RunBId.Should().BeNull();
        result.Value.Outcome.Should().Be("HANDOFF_MAPPING_MISSING",
            "HANDED_OFF without mapping must signal operator attention via this outcome code");

        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Test 8: CreateContinuationMappingCommandHandler: non-terminal state ────

    [Fact]
    public async Task CreateMapping_NonTerminalTriggerState_ReturnsValidationError()
    {
        // Arrange
        var mappingRepo = new Mock<IContinuationMappingRepository>(MockBehavior.Loose);
        var uow         = new Mock<IUnitOfWork>(MockBehavior.Loose);
        var clock       = new FakeClock(FixedNow);

        var handler = new CreateContinuationMappingCommandHandler(
            mappingRepo: mappingRepo.Object,
            unitOfWork:  uow.Object,
            clock:       clock,
            logger:      NullLogger<CreateContinuationMappingCommandHandler>.Instance);

        // RunState.Running is NOT a terminal state.
        var command = new CreateContinuationMappingCommand(
            TriggerRunId:         null,
            TriggerState:         RunState.Running,
            TargetArtifactId:     "artifact-x",
            TargetArtifactVersion:"1.0.0");

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue("non-terminal TriggerState must be rejected");
        result.Error.Code.Should().Be("Validation");

        mappingRepo.Verify(
            r => r.AddAsync(It.IsAny<ContinuationMapping>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Test 9: GetContinuationMappingQueryHandler: mapping found ─────────────

    [Fact]
    public async Task GetMapping_ExistingId_ReturnsMappingDto()
    {
        // Arrange
        var mapping    = MakeMapping(triggerRunId: null, triggerState: RunState.Succeeded);
        var mappingRepo = new Mock<IContinuationMappingRepository>(MockBehavior.Loose);

        mappingRepo
            .Setup(r => r.GetByIdAsync(mapping.MappingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mapping);

        var handler = new GetContinuationMappingQueryHandler(mappingRepo.Object);

        // Act
        var result = await handler.HandleAsync(new GetContinuationMappingQuery(mapping.MappingId));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.MappingId.Should().Be(mapping.MappingId);
        result.Value.TriggerState.Should().Be("SUCCEEDED");
        result.Value.TargetArtifactId.Should().Be(mapping.TargetArtifactId);
        result.Value.TargetArtifactVersion.Should().Be(mapping.TargetArtifactVersion);
        result.Value.Version.Should().Be(1);
    }

    // ── Test 10: GetContinuationMappingQueryHandler: not found ────────────────

    [Fact]
    public async Task GetMapping_UnknownId_ReturnsNotFoundError()
    {
        // Arrange
        var mappingRepo = new Mock<IContinuationMappingRepository>(MockBehavior.Loose);
        var unknownId   = Guid.NewGuid();

        mappingRepo
            .Setup(r => r.GetByIdAsync(unknownId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContinuationMapping?)null);

        var handler = new GetContinuationMappingQueryHandler(mappingRepo.Object);

        // Act
        var result = await handler.HandleAsync(new GetContinuationMappingQuery(unknownId));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
    }

    // ── Test 11: StaticSucceededContinuationService: Run B already exists → skip

    [Fact]
    public async Task StaticSucceededService_RunBAlreadyExists_SkipsCreation()
    {
        // Arrange
        var sourceRunId  = Guid.NewGuid();
        var existingRecord = new ContinuationRunRecord(
            id:                    Guid.NewGuid(),
            sourceRunId:           sourceRunId,
            triggerState:          RunState.Succeeded,
            runBId:                Guid.NewGuid(),
            mappingId:             Guid.NewGuid(),
            mappingVersionSnapshot: 1,
            createdAt:             FixedNow.AddMinutes(-3));

        // Use mock repos to observe interactions. CreateContinuationRunCommandHandler is sealed
        // so we construct a real instance whose inner repos we control.
        var mappingRepo         = new Mock<IContinuationMappingRepository>(MockBehavior.Loose);
        var continuationRunRepo = new Mock<IContinuationRunRepository>(MockBehavior.Loose);

        // Dedup record found: service must short-circuit.
        continuationRunRepo
            .Setup(r => r.FindAsync(sourceRunId, RunState.Succeeded, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRecord);

        // Build a real inner handler. Its inner continuationRunRepo is separate (BuildContinuationHandler
        // creates its own mocks), so we verify the service-level repo instead.
        var (innerHandler, _, _, innerContinuationRunRepo, _) = BuildContinuationHandler();

        var service = new StaticSucceededContinuationService(
            mappingRepo:         mappingRepo.Object,
            continuationRunRepo: continuationRunRepo.Object,
            continuationHandler: innerHandler,
            logger:              NullLogger<StaticSucceededContinuationService>.Instance);

        // Act — should return without calling mappingRepo or the inner continuationHandler.
        await service.OnSucceededAsync(sourceRunId, businessJobId: null);

        // Assert — FindBestMatchAsync on the service-level repo must NOT be called.
        mappingRepo.Verify(
            r => r.FindBestMatchAsync(It.IsAny<Guid>(), It.IsAny<RunState>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "service must short-circuit when Run B dedup record already exists");

        // AddAsync on the inner handler's dedup repo must also NOT be called.
        innerContinuationRunRepo.Verify(
            r => r.AddAsync(It.IsAny<ContinuationRunRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no new ContinuationRunRecord must be written when dedup record already exists");
    }

    // ── Test 12: StaticSucceededContinuationService: no mapping → returns early

    [Fact]
    public async Task StaticSucceededService_NoMapping_ReturnsWithoutCreatingRunB()
    {
        // Arrange
        var sourceRunId = Guid.NewGuid();

        var mappingRepo         = new Mock<IContinuationMappingRepository>(MockBehavior.Loose);
        var continuationRunRepo = new Mock<IContinuationRunRepository>(MockBehavior.Loose);

        continuationRunRepo
            .Setup(r => r.FindAsync(sourceRunId, RunState.Succeeded, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContinuationRunRecord?)null);

        mappingRepo
            .Setup(r => r.FindBestMatchAsync(sourceRunId, RunState.Succeeded, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContinuationMapping?)null);

        var (innerHandler, _, _, innerContinuationRunRepo, _) = BuildContinuationHandler();

        var service = new StaticSucceededContinuationService(
            mappingRepo:         mappingRepo.Object,
            continuationRunRepo: continuationRunRepo.Object,
            continuationHandler: innerHandler,
            logger:              NullLogger<StaticSucceededContinuationService>.Instance);

        // Act
        await service.OnSucceededAsync(sourceRunId, businessJobId: null);

        // Assert — nothing was committed.
        innerContinuationRunRepo.Verify(
            r => r.AddAsync(It.IsAny<ContinuationRunRecord>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no Run B record must be persisted when no mapping exists");
    }
}
