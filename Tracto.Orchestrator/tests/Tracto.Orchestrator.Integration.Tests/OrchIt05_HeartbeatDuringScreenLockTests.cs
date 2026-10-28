#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Fixtures;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-05: heartbeat during screen-lock wait.
///
/// RUNNING job. Send 5 heartbeats. Assert run stays RUNNING.
/// Assert no new job dispatched to session.
/// </summary>
public sealed class OrchIt05_HeartbeatDuringScreenLockTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt05_HeartbeatDuringScreenLockTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FiveHeartbeats_RunStaysRunning_NoCancellationRequested()
    {
        var client     = _factory.AuthenticatedClient();
        var workerId   = $"worker-{Guid.NewGuid():N}";
        var artifactId = $"script-orch-it05-{Guid.NewGuid():N}";
        const string session = "screenlock-session-orch-it-05";

        // Setup
        await client.PostAsJsonAsync("/workers/register", new
        {
            workerId,
            runtimeType             = "desktop",
            actionNames             = new[] { "ClickAction" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 300
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

        // Pull + accept (enter RUNNING)
        var pullResp = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId,
            runtimeType          = "desktop",
            capabilityNames      = Array.Empty<string>(),
            interactiveSessionId = session
        });
        pullResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pullBody  = await ParseJsonAsync(pullResp);
        pullBody.GetProperty("result").GetString().Should().Be("JobOffered");
        var attemptId = Guid.Parse(pullBody.GetProperty("attemptId").GetString()!);

        await client.PostAsJsonAsync($"/jobs/{runId}/lease/accept", new { attemptId, workerId });

        // Send 5 heartbeats — no Thread.Sleep; seqNo increments simulate screen-lock wait
        for (long seqNo = 1; seqNo <= 5; seqNo++)
        {
            var hbResp = await client.PostAsJsonAsync($"/jobs/{runId}/heartbeat", new
            {
                attemptId,
                workerId,
                seqNo
            });

            hbResp.StatusCode.Should().Be(HttpStatusCode.OK,
                $"heartbeat {seqNo} must succeed while run is RUNNING");

            var hbBody = await ParseJsonAsync(hbResp);
            hbBody.GetProperty("cancellationRequested").GetBoolean().Should().BeFalse(
                $"cancellationRequested must be false for heartbeat {seqNo} — no cancel was issued");
        }

        // Assert run is still in Running state after 5 heartbeats
        var getResp = await client.GetAsync($"/jobs/{runId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ParseJsonAsync(getResp)).GetProperty("state").GetString()
            .Should().Be("RUNNING", "heartbeats alone must not change run state");

        // Assert: no new job dispatched to the session while current job is RUNNING
        // A second worker with the same session must get NoPendingJob
        var worker2Id = $"worker2-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/workers/register", new
        {
            workerId                = worker2Id,
            runtimeType             = "desktop",
            actionNames             = new[] { "ClickAction" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        });

        // Create a second queued job to ensure there is something to dispatch
        var job2Resp = await client.PostAsJsonAsync("/jobs", new
        {
            scriptArtifactId = artifactId,
            scriptVersion    = "v1",
            schemaVersion    = "1",
            inputRefs        = Array.Empty<string>(),
            secretRefs       = Array.Empty<string>(),
            triggerType      = "Manual"
        });
        job2Resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Worker2 with same session should get NoPendingJob (session occupied by Worker1)
        var pull2Resp = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId             = worker2Id,
            runtimeType          = "desktop",
            capabilityNames      = Array.Empty<string>(),
            interactiveSessionId = session
        });
        pull2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pull2Body = await ParseJsonAsync(pull2Resp);
        pull2Body.GetProperty("result").GetString()
            .Should().Be("NoPendingJob",
                "session is still occupied by Worker1; no job should be dispatched to the same session");
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
