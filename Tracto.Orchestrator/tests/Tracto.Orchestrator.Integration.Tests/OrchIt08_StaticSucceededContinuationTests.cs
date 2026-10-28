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
/// ORCH-IT-08a: SUCCEEDED run fires the continuation exactly once.
///
/// Create static ContinuationMapping (TriggerState=SUCCEEDED). Complete run SUCCEEDED.
/// Assert one Run B QUEUED. Duplicate SUCCEEDED result → still one Run B (dedup).
/// </summary>
public sealed class OrchIt08a_SucceededContinuationFiresOnceTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt08a_SucceededContinuationFiresOnceTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SucceededRun_Fires_ContinuationOnce_And_Dedup_Works()
    {
        var client     = _factory.AuthenticatedClient();
        var workerId   = $"worker-{Guid.NewGuid():N}";
        var artifactId = $"script-orch-it08a-{Guid.NewGuid():N}";
        var contArtifactId = $"cont-orch-it08a-{Guid.NewGuid():N}";

        // Setup
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

        await client.PutAsJsonAsync($"/artifacts/{contArtifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = Convert.ToBase64String("continuation"u8.ToArray()),
            signature       = (string?)null,
            selectorContent = (string?)null
        });

        // Create Run A
        var jobResp = await client.PostAsJsonAsync("/jobs", new
        {
            scriptArtifactId = artifactId,
            scriptVersion    = "v1",
            schemaVersion    = "1",
            inputRefs        = Array.Empty<string>(),
            secretRefs       = Array.Empty<string>(),
            triggerType      = "Manual"
        });
        jobResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var runAId = Guid.Parse((await ParseJsonAsync(jobResp)).GetProperty("runId").GetString()!);

        // Create static ContinuationMapping: TriggerRunId = runAId, TriggerState = Succeeded
        var mappingResp = await client.PostAsJsonAsync("/continuation-mappings", new
        {
            triggerRunId          = runAId,
            triggerState          = "Succeeded",
            targetArtifactId      = contArtifactId,
            targetArtifactVersion = "v1"
        });
        mappingResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Pull + accept → RUNNING
        var (attemptId, _) = await PullAndAcceptAsync(client, workerId, runAId);

        // Complete SUCCEEDED → Run B must be created
        var result1Resp = await client.PostAsJsonAsync($"/jobs/{runAId}/result", new
        {
            attemptId,
            terminalState = "Succeeded"
        });
        result1Resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope1 = _factory.Services.CreateScope();
        var db1 = scope1.ServiceProvider.GetRequiredService<OrchestratorDbContext>();

        var contRecs1 = await db1.ContinuationRunRecords
            .Where(r => r.SourceRunId == runAId)
            .ToListAsync();
        contRecs1.Should().HaveCount(1, "SUCCEEDED run must fire the continuation exactly once");
        var runBId = contRecs1[0].RunBId;

        // Verify Run B is QUEUED
        var runB = await db1.RunRecords.FirstOrDefaultAsync(r => r.Id == runBId);
        runB.Should().NotBeNull();
        runB!.State.Should().Be(Tracto.Orchestrator.Domain.Runs.RunState.Queued);

        // Duplicate SUCCEEDED result — still exactly one Run B (dedup)
        var result2Resp = await client.PostAsJsonAsync($"/jobs/{runAId}/result", new
        {
            attemptId,
            terminalState = "Succeeded"
        });
        result2Resp.StatusCode.Should().Be(HttpStatusCode.OK, "duplicate result is idempotent");

        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var contRecs2 = await db2.ContinuationRunRecords
            .Where(r => r.SourceRunId == runAId)
            .ToListAsync();
        contRecs2.Should().HaveCount(1, "dedup: still exactly one Run B after duplicate SUCCEEDED");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static async Task<(Guid AttemptId, Guid RunId)> PullAndAcceptAsync(
        HttpClient client, string workerId, Guid runId)
    {
        var pullResp = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId,
            runtimeType     = "dotnet",
            capabilityNames = Array.Empty<string>()
        });
        pullResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pullBody  = await ParseJsonAsync(pullResp);
        pullBody.GetProperty("result").GetString().Should().Be("JobOffered");
        var attemptId = Guid.Parse(pullBody.GetProperty("attemptId").GetString()!);

        await client.PostAsJsonAsync($"/jobs/{runId}/lease/accept", new { attemptId, workerId });
        return (attemptId, runId);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}

/// <summary>
/// ORCH-IT-08b: FAILED run does NOT fire the SUCCEEDED continuation mapping.
///
/// Isolated in its own class to prevent DB state leakage from OrchIt08a.
/// </summary>
public sealed class OrchIt08b_FailedDoesNotFireSucceededContinuationTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt08b_FailedDoesNotFireSucceededContinuationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FailedRun_DoesNotFire_SucceededContinuation()
    {
        var client     = _factory.AuthenticatedClient();
        var workerId   = $"worker-{Guid.NewGuid():N}";
        var artifactId = $"script-orch-it08b-{Guid.NewGuid():N}";
        var contArtifactId = $"cont-orch-it08b-{Guid.NewGuid():N}";

        // Setup
        await client.PostAsJsonAsync("/workers/register", new
        {
            workerId,
            runtimeType             = "dotnet",
            actionNames             = new[] { "Run" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        });

        foreach (var (id, payload) in new[]
        {
            (artifactId, "script"),
            (contArtifactId, "continuation")
        })
        {
            await client.PutAsJsonAsync($"/artifacts/{id}/versions/v1", new
            {
                schemaVersion   = "1",
                payload         = Convert.ToBase64String(payload.Length > 0 ? System.Text.Encoding.UTF8.GetBytes(payload) : new byte[1]),
                signature       = (string?)null,
                selectorContent = (string?)null
            });
        }

        var jobResp = await client.PostAsJsonAsync("/jobs", new
        {
            scriptArtifactId = artifactId,
            scriptVersion    = "v1",
            schemaVersion    = "1",
            inputRefs        = Array.Empty<string>(),
            secretRefs       = Array.Empty<string>(),
            triggerType      = "Manual"
        });
        jobResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var runId = Guid.Parse((await ParseJsonAsync(jobResp)).GetProperty("runId").GetString()!);

        // Static mapping fires only on SUCCEEDED
        await client.PostAsJsonAsync("/continuation-mappings", new
        {
            triggerRunId          = runId,
            triggerState          = "Succeeded",
            targetArtifactId      = contArtifactId,
            targetArtifactVersion = "v1"
        });

        var (attemptId, _) = await PullAndAcceptAsync(client, workerId, runId);

        // Complete FAILED — must NOT fire the SUCCEEDED continuation
        var resultResp = await client.PostAsJsonAsync($"/jobs/{runId}/result", new
        {
            attemptId,
            terminalState  = "Failed",
            errorCategory  = "RUNTIME_ERROR"
        });
        resultResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var contRecs = await db.ContinuationRunRecords
            .Where(r => r.SourceRunId == runId)
            .ToListAsync();
        contRecs.Should().BeEmpty("a FAILED result must NOT fire a SUCCEEDED continuation mapping");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static async Task<(Guid AttemptId, Guid RunId)> PullAndAcceptAsync(
        HttpClient client, string workerId, Guid runId)
    {
        var pullResp = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId,
            runtimeType     = "dotnet",
            capabilityNames = Array.Empty<string>()
        });
        pullResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pullBody  = await ParseJsonAsync(pullResp);
        pullBody.GetProperty("result").GetString().Should().Be("JobOffered");
        var attemptId = Guid.Parse(pullBody.GetProperty("attemptId").GetString()!);

        await client.PostAsJsonAsync($"/jobs/{runId}/lease/accept", new { attemptId, workerId });
        return (attemptId, runId);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
