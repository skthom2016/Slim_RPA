#nullable enable
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Artifacts;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Infrastructure.Clock;
using AppResolutionType = Tracto.Orchestrator.Domain.Runs.ResolutionType;

namespace Tracto.Orchestrator.Unit.Tests.Jobs;

/// <summary>
/// Unit tests for <see cref="ResolveUnknownRunCommandHandler"/> — story OC-12.
///
/// Handler behaviour:
///   InvestigatedNoRerun → ResolutionRecord appended; original RunRecord state stays UNKNOWN.
///   ApprovedNewRun      → New RunRecord created and queued; original RunRecord state stays UNKNOWN;
///                         ResolutionRecord.NewRunId references the new run.
///   Empty justification → 400 Validation.
///   Non-UNKNOWN run     → 422 NOT_UNKNOWN_STATE.
///   Auto-replay invariant: UNKNOWN→QUEUED transition without a ResolutionRecord is impossible —
///                          the original RunRecord is NEVER passed to IRunRepository.Update().
///
/// Each test covers exactly one observable behaviour.
/// FakeClock controls time; all repositories are Moq-backed (loose mocks).
/// CreateJobCommandHandler is constructed with its own set of mocked dependencies for
/// ApprovedNewRun scenarios, because it is a concrete (sealed) class.
/// </summary>
public sealed class ResolveUnknownRunCommandTests
{
    // ── Fixed test baseline ───────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 16, 0, 0, TimeSpan.Zero);

    private const string OperatorId    = "operator@example.com";
    private const string Justification = "Investigated: worker crashed mid-run; safe to retry.";
    private const string ArtifactId    = "art-oc12";
    private const string ArtifactVer   = "2.0.0";

    // ── Domain object factories ───────────────────────────────────────────────

    private static RunRecord MakeUnknownRun(Guid runId)
    {
        var run = new RunRecord(
            id:               runId,
            workflowId:       "wf-oc12",
            createdAt:        FixedNow.AddMinutes(-30),
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: ArtifactId,
            scriptVersion:    ArtifactVer,
            schemaVersion:    "1",
            inputRefs:        [],
            secretRefs:       [],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);

        run.Enqueue(FixedNow.AddMinutes(-28));

        var leaseAttemptId = Guid.NewGuid();
        run.AdoptLease(leaseAttemptId, "worker-001", FixedNow.AddMinutes(-25));
        run.Start(leaseAttemptId, FixedNow.AddMinutes(-20));
        run.MarkUnknown("Worker lost contact during execution.", FixedNow.AddMinutes(-5));

        return run;
    }

    private static RunRecord MakeQueuedRun(Guid runId)
    {
        var run = new RunRecord(
            id:               runId,
            workflowId:       "wf-oc12-queued",
            createdAt:        FixedNow.AddMinutes(-10),
            correlationId:    Guid.NewGuid(),
            scriptArtifactId: ArtifactId,
            scriptVersion:    ArtifactVer,
            schemaVersion:    "1",
            inputRefs:        [],
            secretRefs:       [],
            deadline:         null,
            eventId:          null,
            triggerType:      TriggerType.Manual,
            businessJobId:    null);
        run.Enqueue(FixedNow.AddMinutes(-8));
        return run;
    }

    private static ArtifactRecord MakeArtifact()
        => new(
            artifactId:           ArtifactId,
            version:              ArtifactVer,
            schemaVersion:        "1",
            contentHash:          "sha256-deadbeef",
            storedAt:             FixedNow.AddHours(-2),
            signatureValid:       true,
            selectorContent:      null);

    // ── Handler builders ──────────────────────────────────────────────────────

    /// <summary>
    /// Builds the inner <see cref="CreateJobCommandHandler"/> with the supplied mocks.
    /// Needed because CreateJobCommandHandler is a sealed class and cannot be mocked by Moq.
    /// </summary>
    private static CreateJobCommandHandler BuildCreateJobHandler(
        Mock<IRunRepository>        runRepo,
        Mock<IArtifactRepository>   artifactRepo,
        Mock<IJournalRepository>    journalRepo,
        Mock<IUnitOfWork>           uow,
        FakeClock                   clock)
        => new(
            runRepo.Object,
            artifactRepo.Object,
            journalRepo.Object,
            uow.Object,
            clock,
            new Mock<ITelemetryService>().Object,
            NullLogger<CreateJobCommandHandler>.Instance);

    private static ResolveUnknownRunCommandHandler BuildHandler(
        Mock<IRunRepository>        runRepo,
        Mock<IResolutionRepository> resolutionRepo,
        Mock<IJournalRepository>    journalRepo,
        Mock<IUnitOfWork>           uow,
        CreateJobCommandHandler     createJobHandler,
        FakeClock?                  clock = null)
        => new(
            runRepo.Object,
            resolutionRepo.Object,
            journalRepo.Object,
            uow.Object,
            clock ?? new FakeClock(FixedNow),
            createJobHandler,
            NullLogger<ResolveUnknownRunCommandHandler>.Instance);

    /// <summary>
    /// Returns a <see cref="CreateJobCommandHandler"/> wired to a separate artifact repo
    /// that returns a valid artifact, enabling the inner handler to succeed without
    /// requiring the outer test to configure artifact-repo behaviour.
    /// </summary>
    private static CreateJobCommandHandler BuildSucceedingCreateJobHandler(
        Mock<IRunRepository>     runRepo,
        Mock<IJournalRepository> journalRepo,
        Mock<IUnitOfWork>        uow,
        FakeClock                clock)
    {
        var artifactRepo = new Mock<IArtifactRepository>();
        artifactRepo
            .Setup(a => a.GetRecordAsync(ArtifactId, ArtifactVer, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeArtifact());

        return BuildCreateJobHandler(runRepo, artifactRepo, journalRepo, uow, clock);
    }

    // ── 1. InvestigatedNoRerun → ResolutionRecord appended; run stays UNKNOWN ──

    [Fact]
    public async Task HandleAsync_InvestigatedNoRerun_AppendsResolutionRecord()
    {
        // Arrange
        var runId  = Guid.NewGuid();
        var run    = MakeUnknownRun(runId);
        var clock  = new FakeClock(FixedNow);

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "INVESTIGATED_NO_RERUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "INVESTIGATED_NO_RERUN on an UNKNOWN run must succeed");

        resolutionRepo.Verify(
            r => r.AddAsync(
                It.Is<ResolutionRecord>(rec =>
                    rec.RunId         == runId &&
                    rec.Type          == AppResolutionType.InvestigatedNoRerun &&
                    rec.Justification == Justification &&
                    rec.OperatorId    == OperatorId &&
                    rec.NewRunId      == null),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a ResolutionRecord of type InvestigatedNoRerun must be appended");
    }

    [Fact]
    public async Task HandleAsync_InvestigatedNoRerun_OriginalRunStaysUnknown()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeUnknownRun(runId);
        var clock = new FakeClock(FixedNow);

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "INVESTIGATED_NO_RERUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — original run must remain in UNKNOWN state (domain invariant: history immutable).
        run.State.Should().Be(RunState.Unknown,
            because: "the original RunRecord must never be modified by InvestigatedNoRerun resolution");

        // The original run's Update must NOT be called (auto-replay invariant).
        runRepo.Verify(r => r.Update(It.Is<RunRecord>(r => r.Id == runId)), Times.Never,
            "IRunRepository.Update must never be called for the original UNKNOWN run during resolution");
    }

    [Fact]
    public async Task HandleAsync_InvestigatedNoRerun_WritesAuditJournalEntry()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeUnknownRun(runId);
        var clock = new FakeClock(FixedNow);

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "INVESTIGATED_NO_RERUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        journalRepo.Verify(
            r => r.AppendAsync(
                It.Is<JournalEntry>(j => j.EventType == "RUN_RESOLUTION_ADDED" && j.RunId == runId),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a RUN_RESOLUTION_ADDED journal entry must be written for audit (domain invariant 5)");

        uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce,
            "CommitAsync must be called to persist the resolution atomically (domain invariant 5)");
    }

    [Fact]
    public async Task HandleAsync_InvestigatedNoRerun_ReturnsNullNewRunId()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeUnknownRun(runId);
        var clock = new FakeClock(FixedNow);

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "INVESTIGATED_NO_RERUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.NewRunId.Should().BeNull(
            because: "INVESTIGATED_NO_RERUN must not create a new run, so NewRunId must be null");
    }

    // ── 2. ApprovedNewRun → new run created; original still UNKNOWN ────────────

    [Fact]
    public async Task HandleAsync_ApprovedNewRun_CreatesNewRunId()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeUnknownRun(runId);
        var clock = new FakeClock(FixedNow);

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "APPROVED_NEW_RUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "APPROVED_NEW_RUN on an UNKNOWN run must succeed");
        result.Value.NewRunId.Should().NotBeNull(
            because: "APPROVED_NEW_RUN must link the newly created run's Id in the result");
        result.Value.NewRunId.Should().NotBe(runId,
            because: "the new run must be a different run from the original UNKNOWN run");
    }

    [Fact]
    public async Task HandleAsync_ApprovedNewRun_OriginalRunStaysUnknown()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeUnknownRun(runId);
        var clock = new FakeClock(FixedNow);

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "APPROVED_NEW_RUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — domain invariant: the original run is history and must never change state.
        run.State.Should().Be(RunState.Unknown,
            because: "the original RunRecord must remain UNKNOWN even after APPROVED_NEW_RUN resolution; " +
                     "only a new RunRecord is created (auto-replay invariant)");
    }

    [Fact]
    public async Task HandleAsync_ApprovedNewRun_ResolutionRecordLinksNewRunId()
    {
        // Arrange
        var runId          = Guid.NewGuid();
        var run            = MakeUnknownRun(runId);
        var clock          = new FakeClock(FixedNow);
        ResolutionRecord? capturedResolution = null;

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        resolutionRepo
            .Setup(r => r.AddAsync(It.IsAny<ResolutionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ResolutionRecord, CancellationToken>((rec, _) => capturedResolution = rec)
            .Returns(Task.CompletedTask);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "APPROVED_NEW_RUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        capturedResolution.Should().NotBeNull(
            because: "a ResolutionRecord must be appended for APPROVED_NEW_RUN");
        capturedResolution!.Type.Should().Be(AppResolutionType.ApprovedNewRun,
            because: "the resolution type must match the command");
        capturedResolution.NewRunId.Should().Be(result.Value.NewRunId,
            because: "ResolutionRecord.NewRunId must be linked to the newly created run");
        capturedResolution.NewRunId.Should().NotBeNull(
            because: "ApprovedNewRun resolution must always have a NewRunId");
    }

    [Fact]
    public async Task HandleAsync_ApprovedNewRun_NewRunIsAddedToRepository()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeUnknownRun(runId);
        var clock = new FakeClock(FixedNow);

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "APPROVED_NEW_RUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — CreateJobCommandHandler calls AddAsync for the new run.
        runRepo.Verify(
            r => r.AddAsync(
                It.Is<RunRecord>(newRun => newRun.Id != runId),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a new RunRecord (with a different Id) must be added to the repository for APPROVED_NEW_RUN");
    }

    [Fact]
    public async Task HandleAsync_ApprovedNewRun_OriginalRunNeverPassedToUpdate()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeUnknownRun(runId);
        var clock = new FakeClock(FixedNow);

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "APPROVED_NEW_RUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — auto-replay invariant: the original run is never passed to Update.
        // Any Update calls must be for the NEW run only (from CreateJobCommandHandler).
        runRepo.Verify(
            r => r.Update(It.Is<RunRecord>(r => r.Id == runId)),
            Times.Never,
            "the original UNKNOWN RunRecord must NEVER be passed to IRunRepository.Update — " +
            "auto-replay invariant: UNKNOWN→QUEUED without a ResolutionRecord is forbidden");
    }

    // ── 3. Empty justification → 400 Validation ───────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task HandleAsync_EmptyJustification_Returns400_ValidationError(string? justification)
    {
        // Arrange
        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();
        var clock          = new FakeClock(FixedNow);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          Guid.NewGuid(),
            ResolutionType: "INVESTIGATED_NO_RERUN",
            Justification:  justification!,
            OperatorId:     OperatorId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "an empty justification must be rejected (400 Validation)");
        result.Error.Code.Should().Be("Validation",
            because: "the error code for a missing justification must be Validation");

        uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never,
            "the transaction must never begin when input validation fails");
        runRepo.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "the run must not even be loaded when justification is missing");
        resolutionRepo.Verify(r => r.AddAsync(It.IsAny<ResolutionRecord>(), It.IsAny<CancellationToken>()), Times.Never,
            "no ResolutionRecord must be written on a validation failure");
    }

    // ── 4. Non-UNKNOWN run → 422 NOT_UNKNOWN_STATE ────────────────────────────

    [Theory]
    [InlineData("Queued")]
    [InlineData("Succeeded")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task HandleAsync_NonUnknownRun_Returns422_NotUnknownStateError(string stateName)
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeQueuedRun(runId);

        // Transition into the requested state where applicable.
        RunRecord targetRun;
        switch (stateName)
        {
            case "Queued":
                targetRun = run;
                break;
            case "Succeeded":
            {
                var leaseId = Guid.NewGuid();
                run.AdoptLease(leaseId, "worker-x", FixedNow.AddMinutes(-5));
                run.Start(leaseId, FixedNow.AddMinutes(-4));
                run.Succeed(leaseId, FixedNow.AddMinutes(-1));
                targetRun = run;
                break;
            }
            case "Failed":
            {
                var leaseId = Guid.NewGuid();
                run.AdoptLease(leaseId, "worker-x", FixedNow.AddMinutes(-5));
                run.Start(leaseId, FixedNow.AddMinutes(-4));
                run.Fail(leaseId, FixedNow.AddMinutes(-1));
                targetRun = run;
                break;
            }
            case "Cancelled":
                run.Cancel(FixedNow.AddMinutes(-1));
                targetRun = run;
                break;
            default:
                throw new InvalidOperationException($"Unknown state: {stateName}");
        }

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();
        var clock          = new FakeClock(FixedNow);

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(targetRun);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "INVESTIGATED_NO_RERUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: $"a run in state {stateName} is not UNKNOWN and must be rejected");
        result.Error.Code.Should().Be("NOT_UNKNOWN_STATE",
            because: "the error code for a non-UNKNOWN run must be NOT_UNKNOWN_STATE (422)");

        uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never,
            "the transaction must not begin when the run is not in UNKNOWN state");
        resolutionRepo.Verify(r => r.AddAsync(It.IsAny<ResolutionRecord>(), It.IsAny<CancellationToken>()), Times.Never,
            "no ResolutionRecord must be appended when the run is not UNKNOWN");
    }

    // ── 5. Run not found → 404 NotFound ──────────────────────────────────────

    [Fact]
    public async Task HandleAsync_RunNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var runId = Guid.NewGuid();

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();
        var clock          = new FakeClock(FixedNow);

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync((RunRecord?)null);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "INVESTIGATED_NO_RERUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(because: "a missing run must produce a failure");
        result.Error.Code.Should().Be("NotFound",
            because: "the error code for a missing run must be NotFound");

        uow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Never,
            "the transaction must not begin when the run does not exist");
        resolutionRepo.Verify(r => r.AddAsync(It.IsAny<ResolutionRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 6. Auto-replay invariant: UNKNOWN→QUEUED without ResolutionRecord is impossible ──

    /// <summary>
    /// Auto-replay invariant (domain invariant 2 + OC-12 spec):
    ///   NO code path in <see cref="ResolveUnknownRunCommandHandler"/> is allowed to
    ///   transition an UNKNOWN RunRecord directly to QUEUED state (which would constitute
    ///   an auto-replay, explicitly forbidden for UNKNOWN runs).
    ///
    /// This test verifies the invariant by:
    ///   1. Setting up a run in UNKNOWN state.
    ///   2. Executing both valid resolution types.
    ///   3. Asserting that after each resolution:
    ///      a) the original RunRecord.State is still UNKNOWN.
    ///      b) IRunRepository.Update was never called with the original run
    ///         (the only legal mutation of the original run's state channel).
    /// </summary>
    [Theory]
    [InlineData("INVESTIGATED_NO_RERUN")]
    [InlineData("APPROVED_NEW_RUN")]
    public async Task HandleAsync_AutoReplayInvariant_OriginalRunNeverTransitionsToQueued(string resolutionType)
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeUnknownRun(runId);
        var clock = new FakeClock(FixedNow);

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: resolutionType,
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert — auto-replay invariant
        result.IsSuccess.Should().BeTrue(
            because: $"a valid resolution ({resolutionType}) on an UNKNOWN run must succeed");

        run.State.Should().Be(RunState.Unknown,
            because:
            $"the auto-replay invariant requires that an UNKNOWN RunRecord NEVER transitions to " +
            $"QUEUED (or any other state) as a side-effect of resolution type {resolutionType}. " +
            $"Only a brand-new RunRecord may be queued (APPROVED_NEW_RUN path).");

        // The original run must never be passed to Update — that is the only way its persisted
        // state could change in EF Core. If Update were called the state transition would be
        // saved to the database, violating the invariant.
        runRepo.Verify(
            r => r.Update(It.Is<RunRecord>(r => r.Id == runId)),
            Times.Never,
            $"IRunRepository.Update must NEVER be called with the original UNKNOWN run (id={runId}) " +
            $"during resolution type {resolutionType}. Doing so would enable auto-replay, " +
            "which is forbidden for UNKNOWN runs (domain invariant 2 + OC-12 spec).");
    }

    // ── 7. ResolutionId is non-empty in success result ────────────────────────

    [Fact]
    public async Task HandleAsync_InvestigatedNoRerun_ResultContainsNonEmptyResolutionId()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeUnknownRun(runId);
        var clock = new FakeClock(FixedNow);

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>();

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "INVESTIGATED_NO_RERUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ResolutionId.Should().NotBe(Guid.Empty,
            because: "the handler must return the ResolutionRecord.Id so the API can reference it");
        result.Value.RunId.Should().Be(runId,
            because: "the result must echo back the RunId that was resolved");
    }

    // ── 8. Commit is called after all writes ─────────────────────────────────

    [Fact]
    public async Task HandleAsync_InvestigatedNoRerun_CommitsAtomically()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var run   = MakeUnknownRun(runId);
        var clock = new FakeClock(FixedNow);

        var runRepo        = new Mock<IRunRepository>();
        var resolutionRepo = new Mock<IResolutionRepository>();
        var journalRepo    = new Mock<IJournalRepository>();
        var uow            = new Mock<IUnitOfWork>(MockBehavior.Loose);

        runRepo.Setup(r => r.GetByIdAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(run);

        var createJobHandler = BuildSucceedingCreateJobHandler(runRepo, journalRepo, uow, clock);
        var handler          = BuildHandler(runRepo, resolutionRepo, journalRepo, uow, createJobHandler, clock);

        var cmd = new ResolveUnknownRunCommand(
            RunId:          runId,
            ResolutionType: "INVESTIGATED_NO_RERUN",
            Justification:  Justification,
            OperatorId:     OperatorId);

        // Capture call order.
        var callOrder = new List<string>();
        resolutionRepo
            .Setup(r => r.AddAsync(It.IsAny<ResolutionRecord>(), It.IsAny<CancellationToken>()))
            .Callback<ResolutionRecord, CancellationToken>((_, _) => callOrder.Add("AddResolution"))
            .Returns(Task.CompletedTask);
        journalRepo
            .Setup(r => r.AppendAsync(
                It.Is<JournalEntry>(j => j.EventType == "RUN_RESOLUTION_ADDED"),
                It.IsAny<CancellationToken>()))
            .Callback<JournalEntry, CancellationToken>((_, _) => callOrder.Add("AppendJournal"))
            .Returns(Task.CompletedTask);
        uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
           .Callback<CancellationToken>(_ => callOrder.Add("Commit"))
           .Returns(Task.CompletedTask);

        // Act
        await handler.HandleAsync(cmd);

        // Assert — resolution must be written BEFORE commit (atomicity invariant 5).
        callOrder.Should().ContainInOrder(new[] { "AddResolution", "AppendJournal", "Commit" },
            because: "the ResolutionRecord and journal entry must be staged before CommitAsync to satisfy domain invariant 5");
    }
}
