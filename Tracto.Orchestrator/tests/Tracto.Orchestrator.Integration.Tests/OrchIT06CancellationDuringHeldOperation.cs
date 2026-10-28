#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Shared;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-06: cancellation during held operation.
///
/// AC:
///   RUNNING job. POST /cancel. Next heartbeat returns cancellationRequested=true.
///   Worker submits CANCELLED result. Assert Orchestrator records CANCELLED.
/// </summary>
public sealed class OrchIt06CancellationDuringHeldOperation
    : IClassFixture<OrchIt06CancellationDuringHeldOperation.Factory>
{
    public sealed class Factory : OrchestratorTestFactory { }

    private readonly HttpClient _client;

    public OrchIt06CancellationDuringHeldOperation(Factory factory)
    {
        _client = factory.AuthenticatedClient();
    }

    [Fact]
    public async Task Cancel_RunningJob_NextHeartbeat_ReturnsCancellationRequested()
    {
        // Arrange — drive run to RUNNING.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc06", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc06", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc06", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc06", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        var leaseJson = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc06");
        var runningAttemptId = leaseJson.GetProperty("attemptId").GetGuid();

        // Heartbeat before cancel — should be cancellationRequested=false.
        var hb1 = await ApiHelpers.SendHeartbeatAsync(
            _client, offeredRunId, runningAttemptId, "worker-oc06", seqNo: 1);
        hb1.GetProperty("cancellationRequested").GetBoolean().Should().BeFalse();

        // Act — cancel the running job.
        var cancelResp = await _client.PostAsJsonAsync(
            $"/jobs/{offeredRunId}/cancel",
            new { requestedBy = "operator-1" });
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "cancellation of a RUNNING job must return 200");

        // Assert — next heartbeat returns cancellationRequested=true.
        var hb2 = await ApiHelpers.SendHeartbeatAsync(
            _client, offeredRunId, runningAttemptId, "worker-oc06", seqNo: 2);
        hb2.GetProperty("cancellationRequested").GetBoolean().Should().BeTrue(
            because: "heartbeat after cancel must signal cancellation to the worker");
    }

    [Fact]
    public async Task Worker_SubmitsCancelled_AfterCancellationRequested_StateIsCancelled()
    {
        // Arrange — drive run to RUNNING.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc06b", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc06b", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc06b", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc06b", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        var leaseJson = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc06b");
        var runningAttemptId = leaseJson.GetProperty("attemptId").GetGuid();

        // Cancel the run.
        await _client.PostAsJsonAsync(
            $"/jobs/{offeredRunId}/cancel",
            new { requestedBy = "operator-2" });

        // Worker discovers cancellation on heartbeat.
        var hb = await ApiHelpers.SendHeartbeatAsync(
            _client, offeredRunId, runningAttemptId, "worker-oc06b", seqNo: 1);
        hb.GetProperty("cancellationRequested").GetBoolean().Should().BeTrue();

        // Worker submits CANCELLED result.
        var resultResp = await ApiHelpers.CommitResultAsync(
            _client, offeredRunId, runningAttemptId, "Cancelled");

        resultResp.GetProperty("terminalState").GetString().Should().Be("CANCELLED");

        // Assert — run is in CANCELLED state.
        var jobJson = await ApiHelpers.GetJobAsync(_client, offeredRunId);
        jobJson.GetProperty("state").GetString().Should().Be("CANCELLED");
    }

    [Fact]
    public async Task CancelQueued_RunTransitionsToCancelled_Immediately()
    {
        // Arrange — create a job but do NOT pull it (it stays QUEUED).
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc06c", "1.0");
        await ApiHelpers.CreateJobAsync(_client, "art-oc06c", "1.0");
        var jobJson = await ApiHelpers.CreateJobAsync(_client, "art-oc06c", "1.0");
        var runId   = jobJson.GetProperty("runId").GetGuid();

        // Act — cancel the QUEUED run.
        var cancelResp = await _client.PostAsJsonAsync(
            $"/jobs/{runId}/cancel",
            new { requestedBy = "integration-test" });
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert — run immediately moves to CANCELLED.
        var summary = await ApiHelpers.GetJobAsync(_client, runId);
        summary.GetProperty("state").GetString().Should().Be("CANCELLED");
    }
}
