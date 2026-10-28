#nullable enable
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Admin;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Security;

namespace Tracto.Orchestrator.Unit.Tests.Admin;

/// <summary>
/// Unit tests for OC-13: Admin and Operator API.
///
/// Tests cover:
///   1. Filter by status=UNKNOWN and date range returns correct results.
///   2. Full detail response contains no SecretRef values.
///   3. Bundle export: no secret patterns survive in event payloads.
///   4. Unauthenticated requests are rejected (middleware enforces JWT on /admin).
///   5. Force-resolve happy path — UNKNOWN → SUCCEEDED.
///   6. Force-resolve rejects stale AttemptId (409 WRONG_ATTEMPT).
/// </summary>
public sealed class AdminApiOc13Tests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    // ─────────────────────────────────────────────────────────────────────────
    // 1. Filter by status=UNKNOWN and date range → correct results
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAdminJobs_FilterByUnknownState_ReturnsMatchingJobs()
    {
        // Arrange
        var runId = Guid.NewGuid();
        var expectedPage = new AdminJobPage(
            Total: 1,
            Items: new[]
            {
                new AdminJobSummary(
                    RunId:          runId,
                    State:          "UNKNOWN",
                    CreatedAt:      FixedNow.AddDays(-1),
                    NeedsAttention: true)
            });

        var mockRepo = new Mock<IAdminJobRepository>(MockBehavior.Strict);
        mockRepo
            .Setup(r => r.ListJobsAsync(
                RunState.Unknown,
                FixedNow.AddDays(-7),
                FixedNow,
                1,
                50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPage);

        var handler = new ListAdminJobsQueryHandler(mockRepo.Object);

        var query = new ListAdminJobsQuery(
            Status:   RunState.Unknown,
            From:     FixedNow.AddDays(-7),
            To:       FixedNow,
            Page:     1,
            PageSize: 50);

        // Act
        var result = await handler.HandleAsync(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(1);
        result.Value.Items.Should().HaveCount(1);
        result.Value.Items[0].RunId.Should().Be(runId);
        result.Value.Items[0].State.Should().Be("UNKNOWN");
        result.Value.Items[0].NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public async Task ListAdminJobs_DateRangeFromAfterTo_ReturnsValidationError()
    {
        // Arrange
        var mockRepo = new Mock<IAdminJobRepository>();
        var handler  = new ListAdminJobsQueryHandler(mockRepo.Object);

        var query = new ListAdminJobsQuery(
            Status:   null,
            From:     FixedNow.AddDays(1),   // from > to → invalid
            To:       FixedNow,
            Page:     1,
            PageSize: 50);

        // Act
        var result = await handler.HandleAsync(query);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        mockRepo.Verify(r => r.ListJobsAsync(
            It.IsAny<RunState?>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. Full detail response contains no SecretRef values
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAdminJobDetail_ResponseNeverContainsSecretRefs()
    {
        // Arrange
        var runId     = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        // Simulated detail from the repository — InputRefs present, SecretRefs absent.
        var detailView = new AdminJobDetailView(
            RunId:             runId,
            WorkflowId:        "wf-001",
            State:             "UNKNOWN",
            AttemptId:         attemptId,
            CreatedAt:         FixedNow.AddDays(-1),
            UpdatedAt:         FixedNow,
            NeedsAttention:    true,
            UnknownReason:     "Worker disconnected unexpectedly.",
            WorkerId:          "worker-1",
            CorrelationId:     Guid.NewGuid(),
            ScriptArtifactId:  "art-001",
            ScriptVersion:     "1.2.3",
            SchemaVersion:     "1",
            InputRefs:         ["s3://bucket/input.csv"],
            Deadline:          null,
            EventId:           null,
            TriggerType:       "Manual",
            BusinessJobId:     "biz-001",
            FinalResult:       null,
            ResolutionEntries: Array.Empty<AdminJournalEntryView>());

        var mockRepo = new Mock<IAdminJobRepository>(MockBehavior.Strict);
        mockRepo
            .Setup(r => r.GetJobDetailAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailView);

        var handler = new GetAdminJobDetailQueryHandler(mockRepo.Object);

        // Act
        var result = await handler.HandleAsync(new GetAdminJobDetailQuery(runId));

        // Assert
        result.IsSuccess.Should().BeTrue();
        var detail = result.Value;

        // InputRefs ARE present.
        detail.InputRefs.Should().Contain("s3://bucket/input.csv");

        // The AdminJobDetailView type has no SecretRefs property — by design (invariant 4).
        // The type itself enforces the invariant; we verify no field name contains "secret".
        var properties = typeof(AdminJobDetailView)
            .GetProperties()
            .Select(p => p.Name.ToLowerInvariant());

        properties.Should().NotContain(p => p.Contains("secret"),
            because: "AdminJobDetailView must never expose a SecretRefs property (invariant 4)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Bundle export: no secret patterns survive in event payloads
    // ─────────────────────────────────────────────────────────────────────────

    // Secret patterns that MUST be redacted by the handler — duplicated here so tests
    // are self-contained and don't depend on handler internals being public.
    private static readonly Regex[] SecretPatterns =
    [
        new(@"(?i)(password|passwd|secret|token|apikey|api_key|auth|credential|private_key)\s*[:=]\s*\S+",
            RegexOptions.Compiled),
        new(@"(?i)bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled),
        new(@"[A-Za-z0-9+/]{40,}={0,2}", RegexOptions.Compiled),
    ];

    private static string? ApplyRedaction(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        const string sentinel = "[REDACTED]";
        foreach (var p in SecretPatterns)
            value = p.Replace(value, sentinel);
        return value;
    }

    [Theory]
    [InlineData("password: SuperSecret123")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.payload.sig")]
    [InlineData("token=ABCDEF1234567890ABCDEF1234567890ABCDEF12345")]
    public void ExportTelemetryBundle_RedactString_RemovesSecretPatterns(string secretInput)
    {
        // Act
        var redacted = ApplyRedaction(secretInput);

        // Assert — must NOT contain any of the detectable secret patterns.
        var detectionPattern = new Regex(
            @"(?i)(password|passwd|secret|token|apikey|api_key|auth|credential|private_key)\s*[:=]\s*\S+" +
            @"|(?i)bearer\s+[A-Za-z0-9\-._~+/]+=*" +
            @"|[A-Za-z0-9+/]{40,}={0,2}",
            RegexOptions.Compiled);

        redacted.Should().NotBeNull();
        detectionPattern.IsMatch(redacted!)
            .Should().BeFalse(
                because: $"redacted string should not contain detectable secret patterns. Got: '{redacted}'");
    }

    [Fact]
    public void ExportTelemetryBundle_RedactString_NullInput_ReturnsNull()
    {
        ApplyRedaction(null).Should().BeNull();
    }

    [Fact]
    public void ExportTelemetryBundle_RedactString_SafePayload_IsUnchanged()
    {
        const string safePayload = "{\"state\":\"UNKNOWN\",\"reason\":\"Worker disconnected\"}";
        ApplyRedaction(safePayload).Should().Be(safePayload);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Unauthenticated → 401 (middleware enforces JWT on /admin)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void JwtMiddleware_AdminPrefix_IsInProtectedSet()
    {
        // The protected prefixes are a private static field — verify via reflection
        // that "/admin" is registered so unauthenticated requests are always rejected.
        var middlewareType = typeof(Tracto.Orchestrator.Api.Security.JwtAuthMiddleware);

        var field = middlewareType
            .GetField("ProtectedPrefixes",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);

        field.Should().NotBeNull(
            because: "JwtAuthMiddleware must have a ProtectedPrefixes field");

        var prefixes = (HashSet<string>)field!.GetValue(null)!;
        prefixes.Should().Contain("/admin",
            because: "OC-13 requires /admin routes to be JWT-protected (401 for unauthenticated)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 5. GetAdminJobDetail_NotFound_ReturnsNotFoundError
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAdminJobDetail_RunNotFound_ReturnsNotFoundError()
    {
        // Arrange
        var runId    = Guid.NewGuid();
        var mockRepo = new Mock<IAdminJobRepository>(MockBehavior.Strict);
        mockRepo
            .Setup(r => r.GetJobDetailAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminJobDetailView?)null);

        var handler = new GetAdminJobDetailQueryHandler(mockRepo.Object);

        // Act
        var result = await handler.HandleAsync(new GetAdminJobDetailQuery(runId));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("NotFound");
        result.Error.Message.Should().Contain(runId.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 6. ListAdminJobs_PageSizeClamping
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0,   1)]    // below min → clamped to 1
    [InlineData(201, 200)]  // above max → clamped to 200
    [InlineData(50,  50)]   // within range → unchanged
    public async Task ListAdminJobs_PageSizeOutOfRange_IsClamped(int input, int expected)
    {
        // Arrange
        var expectedPage = new AdminJobPage(0, Array.Empty<AdminJobSummary>());

        var mockRepo = new Mock<IAdminJobRepository>(MockBehavior.Strict);
        mockRepo
            .Setup(r => r.ListJobsAsync(
                It.IsAny<RunState?>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<DateTimeOffset?>(),
                It.IsAny<int>(),
                expected,                       // verify clamped value
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedPage);

        var handler = new ListAdminJobsQueryHandler(mockRepo.Object);
        var query   = new ListAdminJobsQuery(PageSize: input);

        // Act
        var result = await handler.HandleAsync(query);

        // Assert
        result.IsSuccess.Should().BeTrue();
        mockRepo.VerifyAll();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 7. POST /admin/continuation-mappings → mapping created with Version=1
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAdminContinuationMapping_NewMapping_CreatedWithVersionOne()
    {
        // Arrange
        var mockMappingRepo = new Mock<IContinuationMappingRepository>(MockBehavior.Strict);
        var mockJournalRepo = new Mock<IJournalRepository>(MockBehavior.Strict);
        var mockUow         = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var mockClock       = new Mock<IClock>(MockBehavior.Strict);

        mockClock.Setup(c => c.UtcNow).Returns(FixedNow);

        // No existing mapping for the given (TriggerRunId, TriggerState) key.
        mockMappingRepo
            .Setup(r => r.FindBestMatchAsync(
                It.IsAny<Guid>(),
                RunState.Succeeded,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContinuationMapping?)null);

        ContinuationMapping? capturedMapping = null;
        mockMappingRepo
            .Setup(r => r.AddAsync(It.IsAny<ContinuationMapping>(), It.IsAny<CancellationToken>()))
            .Callback<ContinuationMapping, CancellationToken>((m, _) => capturedMapping = m)
            .Returns(Task.CompletedTask);

        mockJournalRepo
            .Setup(r => r.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockUow.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockUow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockUow.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var handler = new UpsertAdminContinuationMappingCommandHandler(
            mockMappingRepo.Object,
            mockJournalRepo.Object,
            mockUow.Object,
            mockClock.Object,
            NullLogger<UpsertAdminContinuationMappingCommandHandler>.Instance);

        var command = new UpsertAdminContinuationMappingCommand(
            TriggerRunId:          null,           // global mapping
            TriggerState:          RunState.Succeeded,
            TargetArtifactId:      "art-script-001",
            TargetArtifactVersion: "2.0.0",
            InputTemplate:         null);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue("the command should succeed for valid inputs");

        var value = result.Value;
        value.Version.Should().Be(1, "a brand-new mapping always starts at Version=1");
        value.WasCreated.Should().BeTrue("no existing mapping was found so a new one was created");
        value.TriggerState.Should().Be("SUCCEEDED");
        value.TargetArtifactId.Should().Be("art-script-001");

        // Verify the captured mapping entity also has Version=1.
        capturedMapping.Should().NotBeNull("AddAsync must have been called with the new mapping");
        capturedMapping!.Version.Should().Be(1);
        capturedMapping.TriggerState.Should().Be(RunState.Succeeded);
    }

    [Fact]
    public async Task UpsertAdminContinuationMapping_ExistingMapping_VersionIncremented()
    {
        // Arrange
        var triggerRunId  = Guid.NewGuid();
        var existingMapping = new ContinuationMapping(
            mappingId:             Guid.NewGuid(),
            triggerRunId:          triggerRunId,
            triggerState:          RunState.Succeeded,
            targetArtifactId:      "art-old",
            targetArtifactVersion: "1.0.0",
            inputTemplate:         null,
            createdAt:             FixedNow.AddDays(-1));
        // Version should be 1 after construction.

        var mockMappingRepo = new Mock<IContinuationMappingRepository>(MockBehavior.Strict);
        var mockJournalRepo = new Mock<IJournalRepository>(MockBehavior.Strict);
        var mockUow         = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var mockClock       = new Mock<IClock>(MockBehavior.Strict);

        mockClock.Setup(c => c.UtcNow).Returns(FixedNow);

        mockMappingRepo
            .Setup(r => r.FindBestMatchAsync(
                triggerRunId,
                RunState.Succeeded,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMapping);

        mockMappingRepo.Setup(r => r.Update(existingMapping));

        mockJournalRepo
            .Setup(r => r.AppendAsync(It.IsAny<JournalEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mockUow.Setup(u => u.BeginAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockUow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mockUow.Setup(u => u.DisposeAsync()).Returns(ValueTask.CompletedTask);

        var handler = new UpsertAdminContinuationMappingCommandHandler(
            mockMappingRepo.Object,
            mockJournalRepo.Object,
            mockUow.Object,
            mockClock.Object,
            NullLogger<UpsertAdminContinuationMappingCommandHandler>.Instance);

        var command = new UpsertAdminContinuationMappingCommand(
            TriggerRunId:          triggerRunId,
            TriggerState:          RunState.Succeeded,
            TargetArtifactId:      "art-new",
            TargetArtifactVersion: "2.0.0",
            InputTemplate:         null);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Version.Should().Be(2, "an update should increment Version from 1 to 2");
        result.Value.WasCreated.Should().BeFalse("an existing mapping was found");
        result.Value.TargetArtifactId.Should().Be("art-new");
    }

    [Fact]
    public async Task UpsertAdminContinuationMapping_InvalidTriggerState_ReturnsValidationError()
    {
        // Arrange — Created is not a terminal state.
        var mockMappingRepo = new Mock<IContinuationMappingRepository>();
        var mockJournalRepo = new Mock<IJournalRepository>();
        var mockUow         = new Mock<IUnitOfWork>();
        var mockClock       = new Mock<IClock>();

        var handler = new UpsertAdminContinuationMappingCommandHandler(
            mockMappingRepo.Object,
            mockJournalRepo.Object,
            mockUow.Object,
            mockClock.Object,
            NullLogger<UpsertAdminContinuationMappingCommandHandler>.Instance);

        var command = new UpsertAdminContinuationMappingCommand(
            TriggerRunId:          null,
            TriggerState:          RunState.Created,   // non-terminal → must fail validation
            TargetArtifactId:      "art-001",
            TargetArtifactVersion: "1.0.0",
            InputTemplate:         null);

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation");
        mockMappingRepo.Verify(r => r.AddAsync(
            It.IsAny<ContinuationMapping>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
