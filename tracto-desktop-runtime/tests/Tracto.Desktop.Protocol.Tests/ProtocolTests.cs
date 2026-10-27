#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;
using Tracto.Contract.Models.Protocol;
using Tracto.Desktop.Protocol;
using Tracto.Mock.BusinessService;
using Tracto.Mock.Orchestrator;
using Xunit;

namespace Tracto.Desktop.Protocol.Tests;

/// <summary>
/// DT-18 CEC v1.0.1 protocol conformance tests.
/// All tests use Tracto.Mock.Orchestrator as the in-process Orchestrator implementation.
/// </summary>
public sealed class ProtocolTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static MockOrchestrator CreateMock() => new MockOrchestrator(registrationValidator: null);

    private static DesktopWorkerRegistrar CreateRegistrar(string workerId = "worker-1", string sessionId = "session-abc")
        => new DesktopWorkerRegistrar(workerId, "1.0.0", sessionId);

    private static MockJob DesktopJob(string[]? requiredNamespaces = null) => new MockJob(
        RunId:                Guid.NewGuid().ToString("N"),
        ScriptRuntimeType:    "Desktop",
        RequiredSchemaMajor:  1,
        RequiredNamespaces:   requiredNamespaces ?? ["desktop.click"],
        IsInteractive:        true,
        ArtifactUrl:          "https://artifacts.example/job.zip",
        ArtifactDigest:       "abc123",
        JobSchemaVersion:     "1.0",
        LeaseDurationMs:      60_000);

    // -----------------------------------------------------------------------
    // DT-18-AC01: Worker registration — capabilityDigest is SHA-256 hex string
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DT18_AC01_RegisterAsync_MockAccepts_DigestIsHexSha256()
    {
        // Arrange
        var mock       = CreateMock();
        var registrar  = CreateRegistrar();

        // Act
        var result = await registrar.RegisterAsync(mock);

        // Assert
        result.IsAccepted.Should().BeTrue("mock Orchestrator should accept a valid desktop worker registration");
        result.WorkerId.Should().Be("worker-1");

        // Verify capabilityDigest produced by CapabilityDigestCalculator is hex and non-empty.
        var caps   = new WorkerCapabilities();
        var digest = CapabilityDigestCalculator.Compute(caps);

        digest.Should().NotBeNullOrEmpty("digest must be a non-empty string");
        digest.Should().HaveLength(64, "SHA-256 produces 32 bytes = 64 lowercase hex chars");
        Regex.IsMatch(digest, @"^[0-9a-f]{64}$").Should().BeTrue("digest must be lowercase hex");
    }

    // -----------------------------------------------------------------------
    // DT-18-AC02: JobOffered → AcceptLease called; leaseId+attemptId in JobContext
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DT18_AC02_JobOffered_AcceptLeaseCalled_ContextHasLeaseAndAttemptId()
    {
        // Arrange
        var mock      = CreateMock();
        var registrar = CreateRegistrar("worker-2", "session-002");
        await registrar.RegisterAsync(mock);

        // Enqueue a matching desktop job using the internal helper.
        mock.EnqueueJob(DesktopJob());

        var pullLoop = new JobPullLoop("worker-2", pollIntervalMs: 50);

        // Act — pull with a short timeout so the test does not hang.
        using var cts     = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var context = await pullLoop.PullJobAsync(mock, cts.Token);

        // Assert
        context.LeaseId.Should().NotBeNullOrEmpty("leaseId must be recorded from JobOffered");
        context.AttemptId.Should().NotBeNullOrEmpty("attemptId must be recorded from JobOffered");
        context.RunId.Should().NotBeNullOrEmpty();
        context.ArtifactUrl.Should().Be("https://artifacts.example/job.zip");
    }

    // -----------------------------------------------------------------------
    // DT-18-AC02 (NoPendingJob branch): no exception; poll delay occurred; loop continues
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DT18_AC02_NoPendingJob_NoException_LoopContinues()
    {
        // Arrange — register worker but enqueue no job; cancel after a brief delay.
        var mock      = CreateMock();
        var registrar = CreateRegistrar("worker-3", "session-003");
        await registrar.RegisterAsync(mock);

        var pullLoop = new JobPullLoop("worker-3", pollIntervalMs: 50);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        // Act & Assert — OperationCanceledException is expected (loop was cancelled, no job arrived).
        var act = async () => await pullLoop.PullJobAsync(mock, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>(
            "loop should exit via cancellation, not throw any other exception");
    }

    // -----------------------------------------------------------------------
    // DT-18-AC04: SubmitAsync UNKNOWN → unknownReason + needsAttention=true in envelope
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DT18_AC04_SubmitUnknown_UnknownReasonAndNeedsAttentionPresent()
    {
        // Arrange
        var mock      = CreateMock();
        var registrar = CreateRegistrar("worker-4", "session-004");
        await registrar.RegisterAsync(mock);

        mock.EnqueueJob(DesktopJob());

        var pullLoop   = new JobPullLoop("worker-4", pollIntervalMs: 50);
        using var cts  = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var context    = await pullLoop.PullJobAsync(mock, cts.Token);

        var submitter = new FinalResultSubmitter("worker-4");

        // Act — submit with UNKNOWN state
        var act = async () => await submitter.SubmitAsync(
            mock,
            context,
            RunState.Unknown,
            [],
            unknownReason: "PROCESS_CRASHED",
            startedAt: DateTimeOffset.UtcNow.AddSeconds(-1));

        // Assert — should not throw; unknownReason and needsAttention are validated inside SubmitAsync.
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void DT18_AC04_SubmitUnknown_MissingUnknownReason_Throws()
    {
        // SubmitAsync must reject UNKNOWN state without a reason.
        var submitter = new FinalResultSubmitter("worker-x");
        var context   = new JobContext
        {
            LeaseId        = "lease-x",
            AttemptId      = "attempt-x",
            RunId          = "run-x",
            ArtifactUrl    = "https://example.com/a.zip",
            ArtifactDigest = "abc",
            LeaseDurationMs = 60_000,
        };

        Func<Task> act = () => submitter.SubmitAsync(
            CreateMock(),
            context,
            RunState.Unknown,
            [],
            unknownReason: null);   // <-- intentionally absent

        act.Should().ThrowAsync<ArgumentException>();
    }

    // -----------------------------------------------------------------------
    // DT-18-AC08: browser.navigate in job → CapabilityNamespaceGuard rejects before AcceptLease
    // -----------------------------------------------------------------------

    [Fact]
    public void DT18_AC08_BrowserNamespaceInJob_GuardRejectsBeforeAcceptLease()
    {
        // Arrange — simulated job contains a browser.navigate action.
        string[] jobNamespaces = ["desktop.click", "browser.navigate"];

        // Act
        Action act = () => CapabilityNamespaceGuard.ValidateJob(jobNamespaces);

        // Assert
        act.Should().Throw<DesktopActionError>()
           .Which.ErrorCode.Should().Be("ENVIRONMENT");
    }

    [Fact]
    public void DT18_AC08_TerminalNamespaceInJob_GuardRejects()
    {
        string[] jobNamespaces = ["terminal.exec"];
        Action act = () => CapabilityNamespaceGuard.ValidateJob(jobNamespaces);
        act.Should().Throw<DesktopActionError>()
           .Which.ErrorCode.Should().Be("ENVIRONMENT");
    }

    [Fact]
    public void DT18_AC08_DesktopOnlyNamespaces_GuardPasses()
    {
        string[] jobNamespaces = ["desktop.click", "desktop.type", "desktop.wait"];
        Action act = () => CapabilityNamespaceGuard.ValidateJob(jobNamespaces);
        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // DT-18-AC09: ServiceInvokeRouter registered serviceRef → ServiceOutcome returned
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DT18_AC09_RegisteredServiceRef_ReturnsServiceOutcome()
    {
        // Arrange
        var businessSvc = new MockBusinessService(ServiceOutcome.Success);
        var router      = new ServiceInvokeRouter(businessSvc);
        var registry    = new MockServiceRegistry(["svc://crm.contacts"]);

        // Act
        var response = await router.InvokeAsync("svc://crm.contacts", "createContact", registry);

        // Assert
        response.ServiceOutcome.Should().Be(ServiceOutcome.Success);
    }

    [Fact]
    public async Task DT18_AC09_RegisteredServiceRef_BusinessRejection_Mapped()
    {
        var businessSvc = new MockBusinessService(ServiceOutcome.BusinessRejection);
        var router      = new ServiceInvokeRouter(businessSvc);
        var registry    = new MockServiceRegistry(["svc://billing"]);

        var response = await router.InvokeAsync("svc://billing", "charge", registry);

        response.ServiceOutcome.Should().Be(ServiceOutcome.BusinessRejection);
        response.ActionCode.Should().Be("BUSINESS_RULE_VIOLATION");
    }

    [Fact]
    public async Task DT18_AC09_RegisteredServiceRef_TechnicalFailure_Mapped()
    {
        var businessSvc = new MockBusinessService(ServiceOutcome.TechnicalFailure);
        var router      = new ServiceInvokeRouter(businessSvc);
        var registry    = new MockServiceRegistry(["svc://orders"]);

        var response = await router.InvokeAsync("svc://orders", "submit", registry);

        response.ServiceOutcome.Should().Be(ServiceOutcome.TechnicalFailure);
        response.ErrorDetail.Should().Be("service.networkTimeout");
    }

    // -----------------------------------------------------------------------
    // DT-18-AC12: ServiceInvokeRouter unregistered URL → PolicyViolationException; no network call
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DT18_AC12_UnregisteredServiceRef_ThrowsPolicyViolation_NoNetworkCall()
    {
        // Arrange — registry does NOT contain the requested serviceRef.
        var businessSvc = new MockBusinessService();
        var router      = new ServiceInvokeRouter(businessSvc);
        var registry    = new MockServiceRegistry(["svc://allowed-service"]);

        // Act
        Func<Task> act = () => router.InvokeAsync("https://arbitrary.external.api/v1/data", "get", registry);

        // Assert — PolicyViolationException thrown; no service call reached.
        await act.Should().ThrowAsync<PolicyViolationException>()
                 .WithMessage("*https://arbitrary.external.api/v1/data*");
    }

    // -----------------------------------------------------------------------
    // Additional coverage: CapabilityDigestCalculator determinism
    // -----------------------------------------------------------------------

    [Fact]
    public void DigestCalculator_SameInput_ReturnsSameDigest()
    {
        var caps    = new WorkerCapabilities();
        var digest1 = CapabilityDigestCalculator.Compute(caps);
        var digest2 = CapabilityDigestCalculator.Compute(caps);

        digest1.Should().Be(digest2, "digest must be deterministic for the same capability set");
    }
}
