#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Infrastructure.Persistence;
using Tracto.Orchestrator.Integration.Tests.Shared;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-09: operator UNKNOWN resolution.
///
/// AC:
///   Create UNKNOWN run. POST /admin/jobs/{runId}/resolve type=InvestigatedNoRerun.
///   Assert original UNKNOWN immutable. POST type=ApprovedNewRun → assert new RunId, original still UNKNOWN.
/// </summary>
public sealed class OrchIt09OperatorUnknownResolution
    : IClassFixture<OrchIt09OperatorUnknownResolution.Factory>
{
    public sealed class Factory : OrchestratorTestFactory { }

    private readonly Factory    _factory;
    private readonly HttpClient _client;

    public OrchIt09OperatorUnknownResolution(Factory factory)
    {
        _factory = factory;
        _client  = factory.AuthenticatedClient();
    }

    private async Task<(Guid runId, Guid runningAttemptId)> DriveToRunningAsync(string suffix)
    {
        await ApiHelpers.PublishArtifactAsync(_client, $"art-oc09-{suffix}", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, $"worker-oc09-{suffix}", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, $"art-oc09-{suffix}", "1.0");
        var pullJson = await ApiHelpers.PullJobAsync(_client, $"worker-oc09-{suffix}", "dotnet");
        var attemptId   = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();
        var leaseJson   = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, $"worker-oc09-{suffix}");
        return (offeredRunId, leaseJson.GetProperty("attemptId").GetGuid());
    }

    [Fact]
    public async Task InvestigatedNoRerun_OriginalRunRemainsUnknown()
    {
        // Arrange — drive run to UNKNOWN.
        var (runId, runningAttemptId) = await DriveToRunningAsync("a");
        await ApiHelpers.CommitResultAsync(
            _client, runId, runningAttemptId, "Unknown",
            unknownReason: "Power lost mid-transaction");

        // Verify run is UNKNOWN.
        var jobBefore = await ApiHelpers.GetJobAsync(_client, runId);
        jobBefore.GetProperty("state").GetString().Should().Be("UNKNOWN");

        // Act — operator resolves with InvestigatedNoRerun.
        var resolveResp = await ApiHelpers.ResolveUnknownRunRawAsync(
            _client, runId, "INVESTIGATED_NO_RERUN",
            justification: "Checked DB; automation did not execute. No rerun needed.",
            operatorId: "ops-1");
        resolveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolveBody = await resolveResp.Content.ReadFromJsonAsync<JsonElement>();
        resolveBody.GetProperty("runId").GetGuid().Should().Be(runId);
        resolveBody.TryGetProperty("newRunId", out var newRunIdProp).Should().BeTrue();
        // NewRunId is null for InvestigatedNoRerun.
        (newRunIdProp.ValueKind == JsonValueKind.Null || newRunIdProp.ValueKind == JsonValueKind.Undefined)
            .Should().BeTrue("InvestigatedNoRerun must not create a new run");

        // Assert — original run is still UNKNOWN (immutable).
        var jobAfter = await ApiHelpers.GetJobAsync(_client, runId);
        jobAfter.GetProperty("state").GetString().Should().Be("UNKNOWN",
            because: "InvestigatedNoRerun must not change the original run state");
    }

    [Fact]
    public async Task ApprovedNewRun_CreatesNewRunId_OriginalStaysUnknown()
    {
        // Arrange — drive run to UNKNOWN.
        var (runId, runningAttemptId) = await DriveToRunningAsync("b");
        await ApiHelpers.CommitResultAsync(
            _client, runId, runningAttemptId, "Unknown",
            unknownReason: "Worker disconnected during critical section");

        // Act — operator approves a new run.
        var resolveResp = await ApiHelpers.ResolveUnknownRunRawAsync(
            _client, runId, "APPROVED_NEW_RUN",
            justification: "Safe to rerun; idempotent automation.",
            operatorId: "ops-2");
        resolveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolveBody = await resolveResp.Content.ReadFromJsonAsync<JsonElement>();
        resolveBody.GetProperty("runId").GetGuid().Should().Be(runId);

        // Assert — new RunId returned.
        var newRunId = resolveBody.GetProperty("newRunId").GetGuid();
        newRunId.Should().NotBeEmpty("ApprovedNewRun must return a new RunId");
        newRunId.Should().NotBe(runId, "new run must be distinct from the original");

        // Assert — original run still UNKNOWN.
        var origJob = await ApiHelpers.GetJobAsync(_client, runId);
        origJob.GetProperty("state").GetString().Should().Be("UNKNOWN",
            because: "original UNKNOWN run must be immutable after ApprovedNewRun");

        // Assert — new run exists and is QUEUED.
        var newJob = await ApiHelpers.GetJobAsync(_client, newRunId);
        newJob.GetProperty("state").GetString().Should().Be("QUEUED",
            because: "ApprovedNewRun must create a new run in QUEUED state");
    }

    [Fact]
    public async Task GetUnknownDetail_ReturnsExpectedFields()
    {
        // Arrange — create an UNKNOWN run.
        var (runId, runningAttemptId) = await DriveToRunningAsync("c");
        const string reason = "Uncertain outcome after network partition";
        await ApiHelpers.CommitResultAsync(
            _client, runId, runningAttemptId, "Unknown", unknownReason: reason);

        // Act — get the unknown detail.
        var detailResp = await _client.GetAsync($"/admin/jobs/{runId}/unknown-detail");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await detailResp.Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("runId").GetGuid().Should().Be(runId);
        detail.GetProperty("state").GetString().Should().Be("UNKNOWN");
        detail.GetProperty("unknownReason").GetString().Should().Be(reason);
        detail.GetProperty("needsAttention").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_NonUnknownRun_Returns422()
    {
        // Arrange — create a SUCCEEDED run.
        var (runId, runningAttemptId) = await DriveToRunningAsync("d");
        await ApiHelpers.CommitResultAsync(_client, runId, runningAttemptId, "Succeeded");

        // Act — try to resolve a non-UNKNOWN run.
        var resp = await ApiHelpers.ResolveUnknownRunRawAsync(
            _client, runId, "INVESTIGATED_NO_RERUN",
            justification: "Not UNKNOWN — should fail.");
        ((int)resp.StatusCode).Should().Be(422,
            because: "resolving a non-UNKNOWN run must return 422");
    }
}
