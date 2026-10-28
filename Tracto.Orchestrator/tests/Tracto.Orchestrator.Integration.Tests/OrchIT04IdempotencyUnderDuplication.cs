#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Infrastructure.Persistence;
using Tracto.Orchestrator.Integration.Tests.Shared;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-04: idempotency under duplication.
///
/// AC:
///   Replay leaseAccepted×2, heartbeat×2, finalResult×2, callback×2.
///   Assert: RunRecord count=1, FinalResultRecord count=1, slot released once.
/// </summary>
public sealed class OrchIt04IdempotencyUnderDuplication
    : IClassFixture<OrchIt04IdempotencyUnderDuplication.Factory>
{
    public sealed class Factory : OrchestratorTestFactory { }

    private readonly Factory _factory;
    private readonly HttpClient _client;

    public OrchIt04IdempotencyUnderDuplication(Factory factory)
    {
        _factory = factory;
        _client  = factory.AuthenticatedClient();
    }

    [Fact]
    public async Task AcceptLease_DuplicateCall_ReturnsOkBothTimes()
    {
        // Arrange.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc04a", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc04a", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc04a", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc04a", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        // Act — accept the lease twice.
        var lease1 = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc04a");
        var lease2 = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc04a");

        // Assert — both succeed and return the same data.
        lease1.GetProperty("runId").GetGuid().Should().Be(offeredRunId);
        lease2.GetProperty("runId").GetGuid().Should().Be(offeredRunId);
        lease1.GetProperty("attemptId").GetGuid().Should().Be(lease2.GetProperty("attemptId").GetGuid());
    }

    [Fact]
    public async Task Heartbeat_DuplicateSequenceNumber_ReturnsOk()
    {
        // Arrange — drive run to RUNNING.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc04b", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc04b", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc04b", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc04b", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        var leaseJson = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc04b");
        var runningAttemptId = leaseJson.GetProperty("attemptId").GetGuid();

        // Send heartbeat once with seqNo=1.
        var hb1 = await ApiHelpers.SendHeartbeatAsync(
            _client, offeredRunId, runningAttemptId, "worker-oc04b", seqNo: 1);
        hb1.GetProperty("cancellationRequested").GetBoolean().Should().BeFalse();

        // Duplicate heartbeat with same seqNo → idempotent 200 per spec invariant 3.
        var resp = await _client.PostAsJsonAsync(
            $"/jobs/{offeredRunId}/heartbeat",
            new { attemptId = runningAttemptId, workerId = "worker-oc04b", seqNo = 1 });
        ((int)resp.StatusCode).Should().Be(200,
            because: "exact duplicate seqNo must be returned idempotently with 200 per spec invariant 3");
    }

    [Fact]
    public async Task FinalResult_DuplicateSubmission_IdempotentSecondCall_Succeeds()
    {
        // Arrange — drive run to RUNNING.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc04c", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc04c", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc04c", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc04c", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        var leaseJson = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc04c");
        var runningAttemptId = leaseJson.GetProperty("attemptId").GetGuid();

        // Commit result once.
        var result1 = await ApiHelpers.CommitResultAsync(
            _client, offeredRunId, runningAttemptId, "Succeeded");
        result1.GetProperty("terminalState").GetString().Should().Be("SUCCEEDED");

        // Commit result again (same AttemptId) → idempotent 200.
        var resp2 = await ApiHelpers.CommitResultRawAsync(
            _client, offeredRunId, runningAttemptId, "Succeeded");
        resp2.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "duplicate finalResult must be idempotent");

        var result2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        result2.GetProperty("terminalState").GetString().Should().Be("SUCCEEDED");
    }

    [Fact]
    public async Task FinalResult_Duplicated_OnlyOneFinalResultRecordExists()
    {
        // Arrange — drive run to RUNNING.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc04d", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc04d", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc04d", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc04d", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        var leaseJson = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc04d");
        var runningAttemptId = leaseJson.GetProperty("attemptId").GetGuid();

        // Commit twice.
        await ApiHelpers.CommitResultAsync(_client, offeredRunId, runningAttemptId, "Succeeded");
        await ApiHelpers.CommitResultRawAsync(_client, offeredRunId, runningAttemptId, "Succeeded");

        // Assert — only one FinalResultRecord in DB.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var count = await db.FinalResultRecords
            .CountAsync(fr => fr.RunId == offeredRunId);
        count.Should().Be(1, because: "duplicate final-result replay must not create duplicate records");
    }

    [Fact]
    public async Task CreateJob_DuplicateEventId_WithinWindow_ReturnsOriginalRunId()
    {
        // Arrange — publish artifact.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc04e", "1.0");

        const string eventId = "evt-dedup-1234";

        // Create job with eventId.
        var job1Json = await ApiHelpers.CreateJobAsync(
            _client, "art-oc04e", "1.0", eventId: eventId);
        var runId1 = job1Json.GetProperty("runId").GetGuid();

        // Create again with same eventId (within dedup window).
        var job2Json = await ApiHelpers.CreateJobAsync(
            _client, "art-oc04e", "1.0", eventId: eventId);
        var runId2 = job2Json.GetProperty("runId").GetGuid();

        // Assert — same RunId returned (idempotent dedup).
        runId2.Should().Be(runId1,
            because: "duplicate eventId within window must return the original RunId");

        // Assert — only one RunRecord.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var count = await db.RunRecords.CountAsync(r => r.EventId == eventId);
        count.Should().Be(1, because: "duplicate event must not create a second RunRecord");
    }
}
