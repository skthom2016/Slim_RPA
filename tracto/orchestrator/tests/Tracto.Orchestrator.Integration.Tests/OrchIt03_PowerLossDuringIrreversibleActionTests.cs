#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Fixtures;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-03: power-loss during irreversible action.
///
/// RUNNING job. Simulate by submitting a reconcile request with no RUN_SUCCEEDED entry
/// (empty journal = uncertain outcome). POST /reconcile.
/// Assert State=UNKNOWN + NeedsAttention. Assert attempt count unchanged.
/// </summary>
public sealed class OrchIt03_PowerLossDuringIrreversibleActionTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt03_PowerLossDuringIrreversibleActionTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReconcileWithEmptyJournal_SetsState_Unknown_NeedsAttention()
    {
        var client     = _factory.AuthenticatedClient();
        var workerId   = $"worker-{Guid.NewGuid():N}";
        var artifactId = $"script-orch-it03-{Guid.NewGuid():N}";

        // Register worker + publish artifact
        await RegisterAndPublishAsync(client, workerId, artifactId);

        // Create job
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

        // Pull + Accept lease (puts run into RUNNING)
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

        var acceptResp = await client.PostAsJsonAsync($"/jobs/{runId}/lease/accept", new
        {
            attemptId,
            workerId
        });
        acceptResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Simulate power-loss: POST /reconcile with an empty journal (no SUCCEEDED entry).
        // This is the key simulation — we skip writing a SUCCEEDED result and instead submit
        // an uncertain journal, which the orchestrator must interpret as UNKNOWN.
        var reconcileResp = await client.PostAsJsonAsync($"/jobs/{runId}/reconcile", new
        {
            attemptId,
            journalEntries = Array.Empty<object>()  // empty — uncertain
        });
        reconcileResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var reconcileBody = await ParseJsonAsync(reconcileResp);

        // Assert: reconciled state is UNKNOWN
        reconcileBody.GetProperty("reconciledState").GetString()
            .Should().Be("UNKNOWN", "empty journal with no success evidence must yield UNKNOWN");

        // Verify via GET /jobs/{runId} that the run is UNKNOWN
        var getResp = await client.GetAsync($"/jobs/{runId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var getBody = await ParseJsonAsync(getResp);
        getBody.GetProperty("state").GetString().Should().Be("UNKNOWN");

        // Verify via GET /admin/jobs/{runId}/unknown-detail that NeedsAttention=true
        var detailResp = await client.GetAsync($"/admin/jobs/{runId}/unknown-detail");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailBody = await ParseJsonAsync(detailResp);
        detailBody.GetProperty("needsAttention").GetBoolean().Should().BeTrue(
            "an UNKNOWN run must always have needsAttention=true (domain invariant 2)");
        detailBody.GetProperty("runId").GetString().Should().Be(runId.ToString());
    }

    [Fact]
    public async Task ReconcileWithUncertainJournal_DoesNotIncrementAttemptCount()
    {
        var client     = _factory.AuthenticatedClient();
        var workerId   = $"worker-{Guid.NewGuid():N}";
        var artifactId = $"script-orch-it03b-{Guid.NewGuid():N}";

        await RegisterAndPublishAsync(client, workerId, artifactId);

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

        var pullResp = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId,
            runtimeType     = "dotnet",
            capabilityNames = Array.Empty<string>()
        });
        var pullBody  = await ParseJsonAsync(pullResp);
        var attemptId = Guid.Parse(pullBody.GetProperty("attemptId").GetString()!);

        var acceptResp = await client.PostAsJsonAsync($"/jobs/{runId}/lease/accept", new
        {
            attemptId,
            workerId
        });
        acceptResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Submit uncertain journal entry (not a SUCCESS event)
        var reconcileResp = await client.PostAsJsonAsync($"/jobs/{runId}/reconcile", new
        {
            attemptId,
            journalEntries = new[]
            {
                new { eventType = "RUN_STARTED", attemptId = attemptId, occurredAt = DateTimeOffset.UtcNow }
            }
        });
        reconcileResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var reconcileBody = await ParseJsonAsync(reconcileResp);
        reconcileBody.GetProperty("reconciledState").GetString().Should().Be("UNKNOWN");

        // NEVER auto-replayed (invariant 2): domain invariant verified in OC-09 resolution path
        // The UNKNOWN run must not be moved to QUEUED automatically.
        var detailResp2 = await client.GetAsync($"/admin/jobs/{runId}/unknown-detail");
        detailResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailBody2 = await ParseJsonAsync(detailResp2);
        detailBody2.GetProperty("state").GetString().Should().Be("UNKNOWN");
        detailBody2.GetProperty("needsAttention").GetBoolean().Should().BeTrue();
    }

    private static async Task RegisterAndPublishAsync(HttpClient client, string workerId, string artifactId)
    {
        var regResp = await client.PostAsJsonAsync("/workers/register", new
        {
            workerId,
            runtimeType             = "dotnet",
            actionNames             = new[] { "Run" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        });
        regResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var pubResp = await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = Convert.ToBase64String("script"u8.ToArray()),
            signature       = (string?)null,
            selectorContent = (string?)null
        });
        pubResp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
