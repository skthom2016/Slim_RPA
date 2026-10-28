#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Fixtures;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-06: cancellation during held operation.
///
/// RUNNING job. POST /cancel. Next heartbeat returns cancellationRequested=true.
/// Worker submits CANCELLED result. Assert Orchestrator records CANCELLED.
/// </summary>
public sealed class OrchIt06_CancellationDuringHeldOperationTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt06_CancellationDuringHeldOperationTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CancelRunning_HeartbeatReflectsCancellation_WorkerSubmitsCancelled()
    {
        var client     = _factory.AuthenticatedClient();
        var workerId   = $"worker-{Guid.NewGuid():N}";
        var artifactId = $"script-orch-it06-{Guid.NewGuid():N}";

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

        // Pull + accept → RUNNING
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

        // Heartbeat before cancel — cancellationRequested should be false
        var hb1Resp = await client.PostAsJsonAsync($"/jobs/{runId}/heartbeat", new
        {
            attemptId,
            workerId,
            seqNo = 1L
        });
        hb1Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ParseJsonAsync(hb1Resp)).GetProperty("cancellationRequested").GetBoolean().Should().BeFalse();

        // POST /cancel — orchestrator flags the run for cancellation
        var cancelResp = await client.PostAsJsonAsync($"/jobs/{runId}/cancel", new
        {
            requestedBy = "operator-orch-it-06"
        });
        cancelResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Next heartbeat must return cancellationRequested=true
        var hb2Resp = await client.PostAsJsonAsync($"/jobs/{runId}/heartbeat", new
        {
            attemptId,
            workerId,
            seqNo = 2L
        });
        hb2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ParseJsonAsync(hb2Resp)).GetProperty("cancellationRequested").GetBoolean().Should().BeTrue(
            "after a cancel request the next heartbeat must signal cancellationRequested=true to the worker");

        // Worker acknowledges by submitting CANCELLED terminal result
        var resultResp = await client.PostAsJsonAsync($"/jobs/{runId}/result", new
        {
            attemptId,
            terminalState = "Cancelled"
        });
        resultResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resultBody = await ParseJsonAsync(resultResp);
        resultBody.GetProperty("terminalState").GetString()
            .Should().Be("CANCELLED", "worker-submitted CANCELLED must be recorded as the terminal state");

        // Verify via GET /jobs/{runId}
        var getResp = await client.GetAsync($"/jobs/{runId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ParseJsonAsync(getResp)).GetProperty("state").GetString()
            .Should().Be("CANCELLED", "run state must be Cancelled after worker commits CANCELLED result");
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
