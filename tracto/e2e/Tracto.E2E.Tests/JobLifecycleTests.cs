using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tracto.E2E.Tests;

/// <summary>
/// E2E-OC-02..07: Full job lifecycle — register worker, publish artifact,
/// submit job, pull job, accept lease, heartbeat, commit result.
///
/// These tests run sequentially and share state via static fields because
/// each step depends on identifiers returned by the previous step.
/// They verify the end-to-end protocol between a simulated worker (this test)
/// and the live orchestrator service.
/// </summary>
[Collection("E2E")]
public sealed class JobLifecycleTests(E2EFixture fixture) : IClassFixture<E2EFixture>
{
    // ── Shared state across test steps ────────────────────────────────────────
    private const  string WorkerId    = "e2e-worker-001";
    private const  string RuntimeType = "dotnet";
    private const  string ArtifactId  = "e2e-hello-script";
    // Unique per process so re-runs don't hit 409 Conflict on idempotent re-publish
    private static readonly string ArtifactVer = $"1.0.{Guid.NewGuid():N}";
    private const  string ActionName  = "HttpGet";

    // E2E-OC-02: Register a worker
    [Fact]
    public async Task Step01_RegisterWorker_Returns200WithWorkerId()
    {
        var request = new
        {
            workerId                = WorkerId,
            runtimeType             = RuntimeType,
            actionNames             = new[] { ActionName },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        };

        var response = await fixture.OrchestratorClient.PostAsJsonAsync("/workers/register", request, E2EFixture.Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal(WorkerId, body.GetProperty("workerId").GetString());
    }

    // E2E-OC-03: Publish an artifact that the job will reference
    [Fact]
    public async Task Step02_PublishArtifact_Returns201()
    {
        var response = await PutArtifact();
        // 201 on first publish; 409 means the artifact already exists (idempotent re-run).
        Assert.True(
            response.StatusCode == HttpStatusCode.Created ||
            response.StatusCode == HttpStatusCode.Conflict,
            $"Unexpected status: {response.StatusCode}");
    }

    // Helper: publish or re-publish the shared artifact; returns the raw response.
    private async Task<HttpResponseMessage> PutArtifact()
    {
        var scriptJson = JsonSerializer.Serialize(new
        {
            name    = "hello-world-script",
            steps   = new[] { new { action = ActionName, url = fixture.HelloWorldUrl + "/api/hello" } }
        });
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(scriptJson));

        var request = new
        {
            schemaVersion   = "1",
            payload,
            signature       = (string?)null,
            selectorContent = (string?)null
        };

        return await fixture.OrchestratorClient.PutAsJsonAsync(
            $"/artifacts/{ArtifactId}/versions/{ArtifactVer}", request, E2EFixture.Json);
    }

    // E2E-OC-04: Submit a job
    [Fact]
    public async Task Step03_SubmitJob_ReturnsRunIdQueued()
    {
        // Ensure worker + artifact exist (idempotent — 200/409 are both acceptable here)
        await Step01_RegisterWorker_Returns200WithWorkerId();
        await PutArtifact();

        var request = new
        {
            scriptArtifactId = ArtifactId,
            scriptVersion    = ArtifactVer,
            schemaVersion    = "1",
            inputRefs        = Array.Empty<string>(),
            secretRefs       = Array.Empty<string>(),
            deadline         = (DateTime?)null,
            eventId          = (string?)null,
            triggerType      = "Manual"
        };

        var response = await fixture.OrchestratorClient.PostAsJsonAsync("/jobs", request, E2EFixture.Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.NotEqual(Guid.Empty, Guid.Parse(body.GetProperty("runId").GetString()!));
        Assert.Equal("QUEUED", body.GetProperty("state").GetString(), StringComparer.OrdinalIgnoreCase);
    }

    // E2E-OC-05..07: Full pull → accept → heartbeat → result cycle.
    // Uses a per-run unique worker ID so it is always new (no stale leases from prior runs).
    private static readonly string CycleWorkerId = $"e2e-cycle-{Guid.NewGuid():N}";

    [Fact]
    public async Task Step04_FullCycle_PullAcceptHeartbeatResult()
    {
        // Register the dedicated cycle worker
        var regRequest = new
        {
            workerId                = CycleWorkerId,
            runtimeType             = RuntimeType,
            actionNames             = new[] { ActionName },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        };
        var regResp = await fixture.OrchestratorClient.PostAsJsonAsync("/workers/register", regRequest, E2EFixture.Json);
        Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);

        // Ensure artifact exists (201 on first publish, 409 if already exists — both OK)
        await PutArtifact();

        // Submit job
        var jobRequest = new
        {
            scriptArtifactId = ArtifactId,
            scriptVersion    = ArtifactVer,
            schemaVersion    = "1",
            inputRefs        = Array.Empty<string>(),
            secretRefs       = Array.Empty<string>(),
            deadline         = (DateTime?)null,
            eventId          = (string?)null,
            triggerType      = "Manual"
        };
        var jobResp = await fixture.OrchestratorClient.PostAsJsonAsync("/jobs", jobRequest, E2EFixture.Json);
        Assert.Equal(HttpStatusCode.OK, jobResp.StatusCode);
        var jobBody = await jobResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        var runId   = Guid.Parse(jobBody.GetProperty("runId").GetString()!);

        // Pull the job using the cycle worker
        var pullRequest = new
        {
            workerId        = CycleWorkerId,
            runtimeType     = RuntimeType,
            capabilityNames = new[] { ActionName }
        };

        JsonElement pullBody = default;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (attempt > 0) await Task.Delay(200);
            var pullResp = await fixture.OrchestratorClient.PostAsJsonAsync("/jobs/pull", pullRequest, E2EFixture.Json);
            Assert.Equal(HttpStatusCode.OK, pullResp.StatusCode);
            pullBody = await pullResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
            if (pullBody.GetProperty("result").GetString() == "JobOffered") break;
        }

        var result = pullBody.GetProperty("result").GetString();
        Assert.Equal("JobOffered", result);

        // The offered run may be our new job or an older queued job — either is fine.
        var offeredRunId = Guid.Parse(pullBody.GetProperty("runId").GetString()!);
        var attemptId    = Guid.Parse(pullBody.GetProperty("attemptId").GetString()!);
        var leaseId      = Guid.Parse(pullBody.GetProperty("leaseId").GetString()!);

        Assert.NotEqual(Guid.Empty, offeredRunId);
        Assert.NotEqual(Guid.Empty, attemptId);
        Assert.NotEqual(Guid.Empty, leaseId);

        // Accept the lease for whatever run was offered
        var acceptRequest = new { attemptId, workerId = CycleWorkerId };
        var acceptResp = await fixture.OrchestratorClient.PostAsJsonAsync(
            $"/jobs/{offeredRunId}/lease/accept", acceptRequest, E2EFixture.Json);
        var acceptRaw = await acceptResp.Content.ReadAsStringAsync();
        Assert.True(acceptResp.StatusCode == HttpStatusCode.OK,
            $"Accept returned {acceptResp.StatusCode}: {acceptRaw}");
        var acceptBody = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(acceptRaw, E2EFixture.Json);
        Assert.Equal(offeredRunId.ToString(), acceptBody.GetProperty("runId").GetString());

        // Simulated worker: call the Hello World service
        var helloResp = await fixture.HelloWorldClient.GetAsync("/api/hello?name=Tracto");
        Assert.Equal(HttpStatusCode.OK, helloResp.StatusCode);

        // Send a heartbeat
        var heartbeatRequest = new { attemptId, workerId = CycleWorkerId, seqNo = 1 };
        var hbResp = await fixture.OrchestratorClient.PostAsJsonAsync(
            $"/jobs/{offeredRunId}/heartbeat", heartbeatRequest, E2EFixture.Json);
        Assert.Equal(HttpStatusCode.OK, hbResp.StatusCode);
        var hbBody = await hbResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.False(hbBody.GetProperty("cancellationRequested").GetBoolean());

        // Commit the final result — Succeeded
        var resultRequest = new
        {
            attemptId,
            terminalState   = "Succeeded",
            errorCategory   = (string?)null,
            businessOutcome = "HELLO_WORLD_COMPLETE",
            evidenceRefs    = Array.Empty<string>()
        };
        var resultResp = await fixture.OrchestratorClient.PostAsJsonAsync(
            $"/jobs/{offeredRunId}/result", resultRequest, E2EFixture.Json);
        Assert.Equal(HttpStatusCode.OK, resultResp.StatusCode);
        var resultBody = await resultResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal("Succeeded", resultBody.GetProperty("terminalState").GetString(), StringComparer.OrdinalIgnoreCase);
        Assert.True(resultBody.GetProperty("slotReleased").GetBoolean());

        // Verify the final state via GET /jobs/{offeredRunId}
        var getResp = await fixture.OrchestratorClient.GetAsync($"/jobs/{offeredRunId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var getBody = await getResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal("Succeeded", getBody.GetProperty("state").GetString(), StringComparer.OrdinalIgnoreCase);
    }

    // E2E-OC-08: Pull endpoint returns 200 with a valid result field.
    // The queue may hold jobs from other steps, so any of the three results is valid.
    [Fact]
    public async Task Step05_Pull_EndpointReturns200WithValidResult()
    {
        await Step01_RegisterWorker_Returns200WithWorkerId();

        var pullRequest = new
        {
            workerId        = WorkerId,
            runtimeType     = RuntimeType,
            capabilityNames = new[] { ActionName }
        };
        var pullResp = await fixture.OrchestratorClient.PostAsJsonAsync("/jobs/pull", pullRequest, E2EFixture.Json);
        Assert.Equal(HttpStatusCode.OK, pullResp.StatusCode);

        var pullBody = await pullResp.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        var result = pullBody.GetProperty("result").GetString();
        Assert.True(
            result == "NoPendingJob" || result == "QueueFull" || result == "JobOffered",
            $"Unexpected result: {result}");
    }
}
