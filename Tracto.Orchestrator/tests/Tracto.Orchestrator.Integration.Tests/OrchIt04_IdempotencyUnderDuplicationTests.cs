#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Infrastructure.Persistence;
using Tracto.Orchestrator.Integration.Tests.Fixtures;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-04: idempotency under duplication.
///
/// Replay leaseAccepted×2, heartbeat×2, finalResult×2.
/// Assert: RunRecord count=1, FinalResultRecord count=1, slot released once.
///
/// Invariant 3: "Duplicate identical message → idempotent 200."
/// This is distinct from a stale-seqNo rejection (seqNo going backwards after a
/// higher one has been accepted), which must return 409 STALE_SEQ_NO.
/// </summary>
public sealed class OrchIt04_IdempotencyUnderDuplicationTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt04_IdempotencyUnderDuplicationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DuplicateLeaseAccept_IsIdempotent()
    {
        var client = _factory.AuthenticatedClient();
        var (runId, attemptId, workerId) = await CreateRunningJobAsync(client);

        // Accept lease a second time — same attemptId same workerId
        var accept2Resp = await client.PostAsJsonAsync($"/jobs/{runId}/lease/accept", new
        {
            attemptId,
            workerId
        });
        // Should succeed (or return the already-accepted state) — not 409
        // Per spec: duplicate identical message → idempotent 200
        accept2Resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify only one RunRecord exists
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var runCount = await db.RunRecords.CountAsync(r => r.Id == runId);
        runCount.Should().Be(1, "duplicate messages must not create additional RunRecords");
    }

    /// <summary>
    /// Invariant 3 — exact-same-seqNo duplicate: replaying seqNo=1 a second time
    /// BEFORE advancing to a higher seqNo must return 200 (idempotent), not 409.
    /// </summary>
    [Fact]
    public async Task DuplicateHeartbeat_ExactSameSeqNo_Returns200()
    {
        var client = _factory.AuthenticatedClient();
        var (runId, attemptId, workerId) = await CreateRunningJobAsync(client);

        // First heartbeat with seqNo=1 — should be accepted normally
        var hb1 = await client.PostAsJsonAsync($"/jobs/{runId}/heartbeat", new
        {
            attemptId,
            workerId,
            seqNo = 1L
        });
        hb1.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ParseJsonAsync(hb1)).GetProperty("cancellationRequested").GetBoolean().Should().BeFalse();

        // Exact duplicate: same seqNo=1 again, BEFORE any higher seqNo has been accepted.
        // Per invariant 3: "Duplicate identical message → idempotent 200."
        var hbDup = await client.PostAsJsonAsync($"/jobs/{runId}/heartbeat", new
        {
            attemptId,
            workerId,
            seqNo = 1L
        });
        hbDup.StatusCode.Should().Be(HttpStatusCode.OK,
            "replaying the exact same seqNo before any higher seqNo is accepted must be idempotent 200");
        (await ParseJsonAsync(hbDup)).GetProperty("cancellationRequested").GetBoolean().Should().BeFalse();
    }

    /// <summary>
    /// Stale-seqNo rejection: after seqNo=2 has been accepted, replaying seqNo=1
    /// must return 409 STALE_SEQ_NO (not idempotent — a different case from exact duplicate).
    /// </summary>
    [Fact]
    public async Task DuplicateHeartbeat_StaleSeqNoAfterAdvance_Returns409()
    {
        var client = _factory.AuthenticatedClient();
        var (runId, attemptId, workerId) = await CreateRunningJobAsync(client);

        // Two heartbeats with ascending seqNo — both accepted
        var hb1 = await client.PostAsJsonAsync($"/jobs/{runId}/heartbeat", new
        {
            attemptId,
            workerId,
            seqNo = 1L
        });
        hb1.StatusCode.Should().Be(HttpStatusCode.OK);

        var hb2 = await client.PostAsJsonAsync($"/jobs/{runId}/heartbeat", new
        {
            attemptId,
            workerId,
            seqNo = 2L
        });
        hb2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Stale duplicate with seqNo=1 — goes backwards after seqNo=2 was accepted.
        // Per spec: this must be rejected with 409 STALE_SEQ_NO.
        var hbStale = await client.PostAsJsonAsync($"/jobs/{runId}/heartbeat", new
        {
            attemptId,
            workerId,
            seqNo = 1L
        });
        hbStale.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "replaying a seqNo that is lower than the last accepted seqNo must return 409 STALE_SEQ_NO");
    }

    [Fact]
    public async Task DuplicateFinalResult_ProducesOneRecord_SlotReleasedOnce()
    {
        var client = _factory.AuthenticatedClient();
        var (runId, attemptId, workerId) = await CreateRunningJobAsync(client);

        // First result commit
        var result1Resp = await client.PostAsJsonAsync($"/jobs/{runId}/result", new
        {
            attemptId,
            terminalState = "Succeeded"
        });
        result1Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var r1Body = await ParseJsonAsync(result1Resp);
        r1Body.GetProperty("slotReleased").GetBoolean().Should().BeTrue(
            "first commit should release the slot");

        // Second result commit — identical message
        var result2Resp = await client.PostAsJsonAsync($"/jobs/{runId}/result", new
        {
            attemptId,
            terminalState = "Succeeded"
        });
        result2Resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "duplicate finalResult must be idempotent (200)");
        var r2Body = await ParseJsonAsync(result2Resp);
        r2Body.GetProperty("slotReleased").GetBoolean().Should().BeFalse(
            "slot must not be released again on duplicate commit");

        // Verify only one FinalResultRecord in DB
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var finalCount = await db.FinalResultRecords.CountAsync(f => f.RunId == runId);
        finalCount.Should().Be(1, "duplicate finalResult must not create additional FinalResultRecords");
    }

    // ── Shared setup: creates a job and puts it in RUNNING state ─────────────────

    private async Task<(Guid RunId, Guid AttemptId, string WorkerId)> CreateRunningJobAsync(HttpClient client)
    {
        var workerId   = $"worker-{Guid.NewGuid():N}";
        var artifactId = $"script-orch-it04-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/workers/register", new
        {
            workerId,
            runtimeType             = "dotnet",
            actionNames             = new[] { "Run" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        });

        await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = Convert.ToBase64String("script"u8.ToArray()),
            signature       = (string?)null,
            selectorContent = (string?)null
        });

        var jobResp = await client.PostAsJsonAsync("/jobs", new
        {
            scriptArtifactId = artifactId,
            scriptVersion    = "v1",
            schemaVersion    = "1",
            inputRefs        = Array.Empty<string>(),
            secretRefs       = Array.Empty<string>(),
            triggerType      = "Manual"
        });
        var runId = Guid.Parse((await ParseJsonAsync(jobResp)).GetProperty("runId").GetString()!);

        var pullResp  = await client.PostAsJsonAsync("/jobs/pull", new { workerId, runtimeType = "dotnet", capabilityNames = Array.Empty<string>() });
        var pullBody  = await ParseJsonAsync(pullResp);
        var attemptId = Guid.Parse(pullBody.GetProperty("attemptId").GetString()!);

        await client.PostAsJsonAsync($"/jobs/{runId}/lease/accept", new { attemptId, workerId });

        return (runId, attemptId, workerId);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
