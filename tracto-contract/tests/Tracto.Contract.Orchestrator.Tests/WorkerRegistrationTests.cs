#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;
using Tracto.Contract.Validation;
using Tracto.Mock.Orchestrator;
using Xunit;

namespace Tracto.Contract.Orchestrator.Tests;

public sealed class WorkerRegistrationTests
{
    private static MockOrchestrator Sut() => new MockOrchestrator();

    private static WorkerRegistration MakeDesktopWorker(
        string id = "w1",
        string sessionId = "session-1",
        HostClass hostClass = HostClass.Interactive) =>
        new WorkerRegistration(
            WorkerId: id,
            RuntimeType: RuntimeType.Desktop,
            WorkerVersion: "1.0.0",
            ProtocolVersion: "1.0",
            SupportedSchemaMajors: new[] { 1 },
            CapabilityDigest: "sha256:abc",
            HostClass: hostClass,
            InteractiveSessionId: hostClass == HostClass.Interactive ? sessionId : null,
            AdvertisedCapabilities: new AdvertisedCapabilities(
                ActionNamespaces: new[] { "desktop.*" },
                PredicateNamespaces: Array.Empty<string>(),
                MaxIterations: 100),
            Health: new WorkerHealth(WorkerHealthStatus.Ready));

    private static MockJob MakeDesktopJob(string runId = "run-1", bool interactive = true) =>
        new MockJob(
            RunId: runId,
            ScriptRuntimeType: "desktop",
            RequiredSchemaMajor: 1,
            RequiredNamespaces: new[] { "desktop.*" },
            IsInteractive: interactive,
            ArtifactUrl: "https://example.com/artifact.zip",
            ArtifactDigest: "sha256:deadbeef",
            JobSchemaVersion: "1.0.0");

    // ST1-09-AC01: Valid Desktop registration accepted
    [Fact]
    public async Task ValidDesktopRegistration_IsAccepted()
    {
        var sut = Sut();
        var reg = MakeDesktopWorker();
        var result = await ((IOrchestratorServer)sut).RegisterWorkerAsync(reg);
        result.IsAccepted.Should().BeTrue();
        result.WorkerId.Should().Be("w1");
    }

    // ST1-09-AC01: Valid Browser registration accepted
    [Fact]
    public async Task ValidBrowserRegistration_IsAccepted()
    {
        var sut = Sut();
        var reg = new WorkerRegistration(
            WorkerId: "w-browser",
            RuntimeType: RuntimeType.Browser,
            WorkerVersion: "1.0.0",
            ProtocolVersion: "1.0",
            SupportedSchemaMajors: new[] { 1 },
            CapabilityDigest: "sha256:abc",
            HostClass: HostClass.Headless,
            InteractiveSessionId: null,
            AdvertisedCapabilities: new AdvertisedCapabilities(
                ActionNamespaces: new[] { "browser.*" },
                PredicateNamespaces: Array.Empty<string>(),
                MaxIterations: 100),
            Health: new WorkerHealth(WorkerHealthStatus.Ready));
        var result = await ((IOrchestratorServer)sut).RegisterWorkerAsync(reg);
        result.IsAccepted.Should().BeTrue();
    }

    // ST1-09-AC01: Valid Terminal headless registration accepted
    [Fact]
    public async Task ValidTerminalHeadlessRegistration_IsAccepted()
    {
        var sut = Sut();
        var reg = new WorkerRegistration(
            WorkerId: "w-terminal",
            RuntimeType: RuntimeType.Terminal,
            WorkerVersion: "1.0.0",
            ProtocolVersion: "1.0",
            SupportedSchemaMajors: new[] { 1 },
            CapabilityDigest: "sha256:abc",
            HostClass: HostClass.Headless,
            InteractiveSessionId: null,
            AdvertisedCapabilities: new AdvertisedCapabilities(
                ActionNamespaces: new[] { "terminal.*" },
                PredicateNamespaces: Array.Empty<string>(),
                MaxIterations: 100),
            Health: new WorkerHealth(WorkerHealthStatus.Ready));
        var result = await ((IOrchestratorServer)sut).RegisterWorkerAsync(reg);
        result.IsAccepted.Should().BeTrue();
    }

    // ST1-09-AC02: Protocol major 1 accepted
    [Fact]
    public async Task SupportedSchemaMajor_Accepted()
    {
        var validator = await WorkerRegistrationValidator.CreateAsync();
        var sut = new MockOrchestrator(validator);
        var reg = MakeDesktopWorker(); // SupportedSchemaMajors = [1]
        var result = await ((IOrchestratorServer)sut).RegisterWorkerAsync(reg);
        result.IsAccepted.Should().BeTrue();
    }

    // ST1-09-AC02: Empty SupportedSchemaMajors → refused (UNSUPPORTED_SCHEMA_MAJOR)
    [Fact]
    public async Task UnsupportedSchemaMajor_Refused()
    {
        var validator = await WorkerRegistrationValidator.CreateAsync();
        var sut = new MockOrchestrator(validator);
        var reg = new WorkerRegistration(
            WorkerId: "w1",
            RuntimeType: RuntimeType.Desktop,
            WorkerVersion: "1.0.0",
            ProtocolVersion: "1.0",
            SupportedSchemaMajors: Array.Empty<int>(), // empty → no schema major covered
            CapabilityDigest: "sha256:abc",
            HostClass: HostClass.Interactive,
            InteractiveSessionId: "session-1",
            AdvertisedCapabilities: new AdvertisedCapabilities(
                ActionNamespaces: new[] { "desktop.*" },
                PredicateNamespaces: Array.Empty<string>(),
                MaxIterations: 100),
            Health: new WorkerHealth(WorkerHealthStatus.Ready));
        var result = await ((IOrchestratorServer)sut).RegisterWorkerAsync(reg);
        result.IsAccepted.Should().BeFalse();
        result.ErrorCode.Should().Be("UNSUPPORTED_SCHEMA_MAJOR");
    }

    // ST1-09-AC03: runtimeType=browser with desktop namespace → refused (NAMESPACE_RUNTIME_MISMATCH)
    [Fact]
    public async Task BrowserWorkerWithDesktopNamespace_Refused()
    {
        var validator = await WorkerRegistrationValidator.CreateAsync();
        var sut = new MockOrchestrator(validator);
        var reg = new WorkerRegistration(
            WorkerId: "w-browser",
            RuntimeType: RuntimeType.Browser,
            WorkerVersion: "1.0.0",
            ProtocolVersion: "1.0",
            SupportedSchemaMajors: new[] { 1 },
            CapabilityDigest: "sha256:abc",
            HostClass: HostClass.Headless,
            InteractiveSessionId: null,
            AdvertisedCapabilities: new AdvertisedCapabilities(
                ActionNamespaces: new[] { "desktop.*" }, // mismatch: browser runtime + desktop namespace
                PredicateNamespaces: Array.Empty<string>(),
                MaxIterations: 100),
            Health: new WorkerHealth(WorkerHealthStatus.Ready));
        var result = await ((IOrchestratorServer)sut).RegisterWorkerAsync(reg);
        result.IsAccepted.Should().BeFalse();
        result.ErrorCode.Should().Be("NAMESPACE_RUNTIME_MISMATCH");
    }

    // ST1-09-AC04: Two interactive workers with same interactiveSessionId → second pull gets NoJob
    [Fact]
    public async Task TwoInteractiveWorkersSameSession_OnlyOneGetsLease()
    {
        var sut = Sut();
        var server = (IOrchestratorServer)sut;
        var w1 = MakeDesktopWorker("w1", "session-A");
        var w2 = MakeDesktopWorker("w2", "session-A"); // same session
        await server.RegisterWorkerAsync(w1);
        await server.RegisterWorkerAsync(w2);
        // Enqueue two jobs so both workers attempt to pull
        sut.EnqueueJob(MakeDesktopJob("run-1"));
        sut.EnqueueJob(MakeDesktopJob("run-2"));

        var pull1 = await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        var pull2 = await server.PullJobAsync(new PullJobRequest("w2", null, DateTimeOffset.UtcNow));

        var results = new PullJobResponse[] { pull1, pull2 };
        results.OfType<JobOffered>().Should().HaveCount(1, "only one worker may hold the interactive session at a time");
        results.OfType<NoPendingJob>().Should().HaveCount(1);
    }

    // ST1-09-AC05: Headless worker is eligible while an unrelated interactive worker is busy
    [Fact]
    public async Task HeadlessWorker_EligibleWhileInteractiveBusy()
    {
        var sut = Sut();
        var server = (IOrchestratorServer)sut;

        // Register interactive desktop worker
        var wInteractive = MakeDesktopWorker("w-desk", "session-1");
        await server.RegisterWorkerAsync(wInteractive);

        // Register headless browser worker
        var wHeadless = new WorkerRegistration(
            WorkerId: "w-browser",
            RuntimeType: RuntimeType.Browser,
            WorkerVersion: "1.0.0",
            ProtocolVersion: "1.0",
            SupportedSchemaMajors: new[] { 1 },
            CapabilityDigest: "sha256:abc",
            HostClass: HostClass.Headless,
            InteractiveSessionId: null,
            AdvertisedCapabilities: new AdvertisedCapabilities(
                ActionNamespaces: new[] { "browser.*" },
                PredicateNamespaces: Array.Empty<string>(),
                MaxIterations: 100),
            Health: new WorkerHealth(WorkerHealthStatus.Ready));
        await server.RegisterWorkerAsync(wHeadless);

        // Make interactive desktop worker busy by giving it a job
        sut.EnqueueJob(MakeDesktopJob("run-desk", interactive: true));
        var deskPull = await server.PullJobAsync(new PullJobRequest("w-desk", null, DateTimeOffset.UtcNow));
        deskPull.Should().BeOfType<JobOffered>("desktop worker should claim its interactive job");

        // Enqueue a headless browser job
        sut.EnqueueJob(new MockJob(
            RunId: "run-browser",
            ScriptRuntimeType: "browser",
            RequiredSchemaMajor: 1,
            RequiredNamespaces: new[] { "browser.*" },
            IsInteractive: false,
            ArtifactUrl: "https://example.com/artifact.zip",
            ArtifactDigest: "sha256:deadbeef",
            JobSchemaVersion: "1.0.0"));

        var browserPull = await server.PullJobAsync(new PullJobRequest("w-browser", null, DateTimeOffset.UtcNow));
        browserPull.Should().BeOfType<JobOffered>("headless browser worker is eligible while interactive desktop is busy");
    }

    // ST1-09-AC06: Interactive worker without interactiveSessionId → refused
    [Fact]
    public async Task InteractiveWorkerMissingSessionId_Refused()
    {
        var sut = Sut();
        var reg = new WorkerRegistration(
            WorkerId: "w1",
            RuntimeType: RuntimeType.Desktop,
            WorkerVersion: "1.0.0",
            ProtocolVersion: "1.0",
            SupportedSchemaMajors: new[] { 1 },
            CapabilityDigest: "sha256:abc",
            HostClass: HostClass.Interactive,
            InteractiveSessionId: null, // missing — must be refused
            AdvertisedCapabilities: new AdvertisedCapabilities(
                ActionNamespaces: new[] { "desktop.*" },
                PredicateNamespaces: Array.Empty<string>(),
                MaxIterations: 100),
            Health: new WorkerHealth(WorkerHealthStatus.Ready));
        var result = await ((IOrchestratorServer)sut).RegisterWorkerAsync(reg);
        result.IsAccepted.Should().BeFalse();
        result.ErrorCode.Should().Be("MISSING_INTERACTIVE_SESSION_ID");
    }

    // ST1-09-AC07: Worker whose SupportedSchemaMajors doesn't cover job → job withheld
    [Fact]
    public async Task WorkerMissingSchemaMajor_JobWithheld()
    {
        var sut = Sut();
        var server = (IOrchestratorServer)sut;
        // Worker supports only schema major 2; job requires major 1
        var reg = new WorkerRegistration(
            WorkerId: "w1",
            RuntimeType: RuntimeType.Desktop,
            WorkerVersion: "1.0.0",
            ProtocolVersion: "1.0",
            SupportedSchemaMajors: new[] { 2 },
            CapabilityDigest: "sha256:abc",
            HostClass: HostClass.Interactive,
            InteractiveSessionId: "session-1",
            AdvertisedCapabilities: new AdvertisedCapabilities(
                ActionNamespaces: new[] { "desktop.*" },
                PredicateNamespaces: Array.Empty<string>(),
                MaxIterations: 100),
            Health: new WorkerHealth(WorkerHealthStatus.Ready));
        await server.RegisterWorkerAsync(reg);
        sut.EnqueueJob(MakeDesktopJob("run-1")); // job requires schema major 1
        var pull = await server.PullJobAsync(new PullJobRequest("w1", null, DateTimeOffset.UtcNow));
        pull.Should().BeOfType<NoPendingJob>("job requiring schema major 1 must not be dispatched to a schema-major-2-only worker");
    }

    // ST1-09-AC08: capabilityDigest is an opaque integrity hint, not an authentication gate
    [Fact]
    public async Task CapabilityDigestIsOpaqueHint_NotAuthGate()
    {
        var sut = Sut();
        // Any non-null opaque string as digest should be accepted (the mock skips digest auth)
        var reg = new WorkerRegistration(
            WorkerId: "w1",
            RuntimeType: RuntimeType.Desktop,
            WorkerVersion: "1.0.0",
            ProtocolVersion: "1.0",
            SupportedSchemaMajors: new[] { 1 },
            CapabilityDigest: "opaque-non-sha256-value", // arbitrary opaque string
            HostClass: HostClass.Interactive,
            InteractiveSessionId: "session-1",
            AdvertisedCapabilities: new AdvertisedCapabilities(
                ActionNamespaces: new[] { "desktop.*" },
                PredicateNamespaces: Array.Empty<string>(),
                MaxIterations: 100),
            Health: new WorkerHealth(WorkerHealthStatus.Ready));
        var result = await ((IOrchestratorServer)sut).RegisterWorkerAsync(reg);
        result.IsAccepted.Should().BeTrue("capabilityDigest is an integrity hint only, not an authentication gate");
    }
}
