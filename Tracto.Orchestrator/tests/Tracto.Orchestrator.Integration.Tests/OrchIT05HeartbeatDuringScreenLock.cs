#nullable enable
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Shared;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-05: heartbeat during screen-lock wait.
///
/// AC:
///   RUNNING job. Send 5 heartbeats. Assert run stays RUNNING.
///   Assert no new job dispatched to session.
/// </summary>
public sealed class OrchIt05HeartbeatDuringScreenLock
    : IClassFixture<OrchIt05HeartbeatDuringScreenLock.Factory>
{
    public sealed class Factory : OrchestratorTestFactory { }

    private readonly HttpClient _client;

    public OrchIt05HeartbeatDuringScreenLock(Factory factory)
    {
        _client = factory.AuthenticatedClient();
    }

    [Fact]
    public async Task FiveHeartbeats_RunStaysRunning_NoCancellationRequested()
    {
        // Arrange — drive run to RUNNING.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc05", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc05", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc05", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc05", "dotnet");
        pullJson.GetProperty("result").GetString().Should().Be("JobOffered");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        var leaseJson = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc05");
        var runningAttemptId = leaseJson.GetProperty("attemptId").GetGuid();

        // Act — send 5 sequential heartbeats (increasing seqNo).
        for (int i = 1; i <= 5; i++)
        {
            var hbJson = await ApiHelpers.SendHeartbeatAsync(
                _client, offeredRunId, runningAttemptId, "worker-oc05", seqNo: i);

            // Assert — each heartbeat returns cancellationRequested=false.
            hbJson.GetProperty("cancellationRequested").GetBoolean().Should().BeFalse(
                because: $"heartbeat #{i} must not signal cancellation when run is healthy");
        }

        // Assert — run is still RUNNING after all heartbeats.
        var jobJson = await ApiHelpers.GetJobAsync(_client, offeredRunId);
        jobJson.GetProperty("state").GetString().Should().Be("RUNNING",
            because: "heartbeats alone must not change the run state");
    }

    [Fact]
    public async Task HeartbeatDuringScreenLock_NoNewJobDispatchedToSameSession()
    {
        // Arrange — drive run to RUNNING with an interactiveSessionId.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc05b", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc05b", "dotnet");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc05c", "dotnet");

        // Create a second job that a competing worker might try to grab.
        await ApiHelpers.CreateJobAsync(_client, "art-oc05b", "1.0");
        await ApiHelpers.CreateJobAsync(_client, "art-oc05b", "1.0");

        // Worker1 pulls with session.
        var pull1 = await ApiHelpers.PullJobAsync(
            _client, "worker-oc05b", "dotnet", interactiveSessionId: "lock-session");
        pull1.GetProperty("result").GetString().Should().Be("JobOffered");
        var attemptId = pull1.GetProperty("attemptId").GetGuid();
        var offeredRunId = pull1.GetProperty("runId").GetGuid();

        var leaseJson = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc05b");
        var runningAttemptId = leaseJson.GetProperty("attemptId").GetGuid();

        // Send 5 heartbeats simulating screen-lock wait.
        for (int i = 1; i <= 5; i++)
        {
            var hb = await ApiHelpers.SendHeartbeatAsync(
                _client, offeredRunId, runningAttemptId, "worker-oc05b", seqNo: i);
            hb.GetProperty("cancellationRequested").GetBoolean().Should().BeFalse();
        }

        // Assert — competing worker cannot pull job for the same session while Worker1 is heartbeating.
        var pull2 = await ApiHelpers.PullJobAsync(
            _client, "worker-oc05c", "dotnet", interactiveSessionId: "lock-session");
        pull2.GetProperty("result").GetString().Should().Be("NoPendingJob",
            because: "session1 is still held by Worker1 during screen-lock wait");
    }

    [Fact]
    public async Task HeartbeatsWithIncreasingSeqNo_AllSucceed()
    {
        // Arrange.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc05d", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc05d", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc05d", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc05d", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();
        var leaseJson = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc05d");
        var runningAttemptId = leaseJson.GetProperty("attemptId").GetGuid();

        // Act — 5 heartbeats with sequentially increasing seqNos.
        long[] seqNos = [10, 20, 30, 40, 50];
        foreach (var seqNo in seqNos)
        {
            var hb = await ApiHelpers.SendHeartbeatAsync(
                _client, offeredRunId, runningAttemptId, "worker-oc05d", seqNo);
            hb.TryGetProperty("cancellationRequested", out _).Should().BeTrue(
                because: "heartbeat response must always contain cancellationRequested");
        }
    }
}
