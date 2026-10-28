#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Shared;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-01: interactiveSessionId exclusivity.
///
/// AC:
///   Register Desktop worker + headed Browser worker, both with interactiveSessionId="session1".
///   Create headed job. Worker1 PullJob → JobOffered. Worker2 PullJob → NoPendingJob.
///   Worker1 completes run (SUCCEEDED). Worker2 PullJob (second job) → JobOffered.
/// </summary>
public sealed class OrchIt01InteractiveSessionExclusivity
    : IClassFixture<OrchIt01InteractiveSessionExclusivity.Factory>
{
    public sealed class Factory : OrchestratorTestFactory { }

    private readonly HttpClient _client;

    public OrchIt01InteractiveSessionExclusivity(Factory factory)
    {
        _client = factory.AuthenticatedClient();
    }

    [Fact]
    public async Task Worker1Pulls_Worker2Blocked_OnSameInteractiveSession()
    {
        // Arrange — publish artifact, register two workers sharing the same session id.
        await ApiHelpers.PublishArtifactAsync(_client, "art-headed", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-desktop", "dotnet");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-browser", "dotnet");

        // Create job #1.
        var job1Json = await ApiHelpers.CreateJobAsync(_client, "art-headed", "1.0");
        var runId1   = job1Json.GetProperty("runId").GetGuid();
        runId1.Should().NotBeEmpty();

        // Worker1 pulls with interactiveSessionId="session1" → JobOffered.
        var pull1 = await ApiHelpers.PullJobAsync(
            _client, "worker-desktop", "dotnet",
            interactiveSessionId: "session1");
        pull1.GetProperty("result").GetString().Should().Be("JobOffered");
        var attemptId1 = pull1.GetProperty("attemptId").GetGuid();

        // Worker2 pulls with same session → NoPendingJob (session locked by Worker1's lease).
        var pull2 = await ApiHelpers.PullJobAsync(
            _client, "worker-browser", "dotnet",
            interactiveSessionId: "session1");
        pull2.GetProperty("result").GetString().Should().Be("NoPendingJob");

        // Worker1 accepts lease, transitions run to RUNNING.
        var offeredRunId1 = pull1.GetProperty("runId").GetGuid();
        var lease1 = await ApiHelpers.AcceptLeaseAsync(
            _client, offeredRunId1, attemptId1, "worker-desktop");
        var runningAttemptId = lease1.GetProperty("attemptId").GetGuid();

        // Worker1 completes the run (SUCCEEDED).
        await ApiHelpers.CommitResultAsync(
            _client, offeredRunId1, runningAttemptId, "Succeeded");

        // Create job #2 for Worker2 to pick up.
        await ApiHelpers.CreateJobAsync(_client, "art-headed", "1.0");

        // Worker2 can now pull job #2 (session1 lease has been released).
        var pull3 = await ApiHelpers.PullJobAsync(
            _client, "worker-browser", "dotnet",
            interactiveSessionId: "session1");
        pull3.GetProperty("result").GetString().Should().Be("JobOffered");
    }

    [Fact]
    public async Task Worker2_CanPull_DifferentSession_WhileWorker1HoldsSession1()
    {
        // Arrange — publish artifact, register workers.
        await ApiHelpers.PublishArtifactAsync(_client, "art-headed-s", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-s1-a", "dotnet");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-s2-a", "dotnet");

        // Create two jobs.
        await ApiHelpers.CreateJobAsync(_client, "art-headed-s", "1.0");
        await ApiHelpers.CreateJobAsync(_client, "art-headed-s", "1.0");

        // Worker1 pulls session1.
        var pull1 = await ApiHelpers.PullJobAsync(
            _client, "worker-s1-a", "dotnet", interactiveSessionId: "session-A");
        pull1.GetProperty("result").GetString().Should().Be("JobOffered");

        // Worker2 pulls session2 (different session — should get offered the second job).
        var pull2 = await ApiHelpers.PullJobAsync(
            _client, "worker-s2-a", "dotnet", interactiveSessionId: "session-B");
        pull2.GetProperty("result").GetString().Should().Be("JobOffered");
    }
}
