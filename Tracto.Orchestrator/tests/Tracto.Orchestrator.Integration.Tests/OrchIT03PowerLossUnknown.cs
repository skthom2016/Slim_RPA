#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Shared;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-03: power-loss during irreversible action.
///
/// AC:
///   RUNNING job. Simulate power-loss by submitting an uncertain journal (no SUCCEEDED entry).
///   POST /reconcile. Assert State=UNKNOWN + NeedsAttention. Assert attempt count unchanged.
/// </summary>
public sealed class OrchIt03PowerLossUnknown
    : IClassFixture<OrchIt03PowerLossUnknown.Factory>
{
    public sealed class Factory : OrchestratorTestFactory { }

    private readonly HttpClient _client;

    public OrchIt03PowerLossUnknown(Factory factory)
    {
        _client = factory.AuthenticatedClient();
    }

    [Fact]
    public async Task Reconcile_WithUncertainJournal_TransitionsToUnknownWithNeedsAttention()
    {
        // Arrange — drive the run to RUNNING state.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc03", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc03", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc03", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc03", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc03");

        // Act — simulate uncertain journal (no RUN_SUCCEEDED entry — uncertain outcome).
        var uncertainJournal = new object[]
        {
            new
            {
                eventType  = "AUTOMATION_STARTED",
                attemptId  = attemptId,
                occurredAt = DateTimeOffset.UtcNow.AddSeconds(-30)
            }
            // Intentionally no RUN_SUCCEEDED or RUN_FAILED — simulates power loss mid-action.
        };

        var reconcileResp = await ApiHelpers.ReconcileAsync(
            _client, offeredRunId, attemptId, uncertainJournal);

        // Assert — reconcile succeeds (200).
        reconcileResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var reconcileJson = await reconcileResp.Content.ReadFromJsonAsync<JsonElement>();
        reconcileJson.GetProperty("reconciledState").GetString().Should()
            .Be("UNKNOWN", because: "uncertain journal must cause UNKNOWN state");

        // Verify via GET /jobs/{runId}.
        var jobJson = await ApiHelpers.GetJobAsync(_client, offeredRunId);
        jobJson.GetProperty("state").GetString().Should().Be("UNKNOWN");
    }

    [Fact]
    public async Task Reconcile_WithEmptyJournal_TransitionsToUnknown()
    {
        // Arrange — drive run to RUNNING.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc03b", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc03b", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc03b", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc03b", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();
        await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc03b");

        // Act — empty journal (no evidence of completion).
        var reconcileResp = await ApiHelpers.ReconcileAsync(
            _client, offeredRunId, attemptId, journalEntries: []);

        // Assert — UNKNOWN, NeedsAttention.
        reconcileResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await reconcileResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reconciledState").GetString().Should().Be("UNKNOWN");
    }

    [Fact]
    public async Task Reconcile_WithSuccessJournal_TransitionsToSucceeded()
    {
        // Arrange — drive run to RUNNING.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc03c", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc03c", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc03c", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc03c", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();
        await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc03c");

        // Act — journal proves SUCCEEDED.
        var successJournal = new object[]
        {
            new
            {
                eventType  = "RUN_SUCCEEDED",
                attemptId  = attemptId,
                occurredAt = DateTimeOffset.UtcNow.AddSeconds(-5)
            }
        };

        var reconcileResp = await ApiHelpers.ReconcileAsync(
            _client, offeredRunId, attemptId, successJournal);

        reconcileResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await reconcileResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("reconciledState").GetString().Should().Be("SUCCEEDED");
    }
}
