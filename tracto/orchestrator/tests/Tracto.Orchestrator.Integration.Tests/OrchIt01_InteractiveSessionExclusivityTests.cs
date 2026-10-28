#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Fixtures;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-01: interactiveSessionId exclusivity.
///
/// Register Desktop worker + headed Browser worker both with interactiveSessionId="session1".
/// Create headed job. Worker1 PullJob → JobOffered. Worker2 PullJob → NoPendingJob.
/// Worker1 completes run (SUCCEEDED). Worker2 PullJob → JobOffered.
/// </summary>
public sealed class OrchIt01_InteractiveSessionExclusivityTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt01_InteractiveSessionExclusivityTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Worker2_CannotPull_WhileWorker1_HoldsInteractiveSession()
    {
        // Arrange
        var client = _factory.AuthenticatedClient();
        var worker1 = $"desktop-{Guid.NewGuid():N}";
        var worker2 = $"browser-{Guid.NewGuid():N}";
        const string session = "session1-orch-it-01";

        // Register Desktop worker1
        var reg1 = await client.PostAsJsonAsync("/workers/register", new
        {
            workerId                = worker1,
            runtimeType             = "desktop",
            actionNames             = new[] { "ClickAction" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        });
        reg1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Register headed Browser worker2 — same interactiveSessionId
        var reg2 = await client.PostAsJsonAsync("/workers/register", new
        {
            workerId                = worker2,
            runtimeType             = "browser",
            actionNames             = new[] { "Navigate" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        });
        reg2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Publish a script artifact for the job
        var artifactId = $"script-orch-it01-{Guid.NewGuid():N}";
        var pubResp = await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion = "1",
            payload       = Convert.ToBase64String("console.log('hello');"u8.ToArray()),
            signature     = (string?)null,
            selectorContent = (string?)null
        });
        pubResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Create a job that requires a desktop runtime
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
        var jobBody = await ParseJsonAsync(jobResp);
        var runId   = Guid.Parse(jobBody.GetProperty("runId").GetString()!);

        // Act — Worker1 pulls the job
        var pull1Resp = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId             = worker1,
            runtimeType          = "desktop",
            capabilityNames      = Array.Empty<string>(),
            interactiveSessionId = session
        });
        pull1Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pull1Body = await ParseJsonAsync(pull1Resp);
        var pull1Result = pull1Body.GetProperty("result").GetString();

        // Worker1 should get the job
        pull1Result.Should().Be("JobOffered");
        var attemptId = Guid.Parse(pull1Body.GetProperty("attemptId").GetString()!);
        var leaseId   = pull1Body.GetProperty("leaseId").GetString();
        leaseId.Should().NotBeNullOrEmpty();

        // Worker2 attempts to pull — should get NoPendingJob (session is held by Worker1)
        var pull2Resp = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId             = worker2,
            runtimeType          = "browser",
            capabilityNames      = Array.Empty<string>(),
            interactiveSessionId = session
        });
        pull2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pull2Body   = await ParseJsonAsync(pull2Resp);
        var pull2Result = pull2Body.GetProperty("result").GetString();

        pull2Result.Should().Be("NoPendingJob",
            "Worker2 must not receive the job while Worker1 holds the interactiveSession slot.");

        // Worker1 accepts the lease
        var acceptResp = await client.PostAsJsonAsync($"/jobs/{runId}/lease/accept", new
        {
            attemptId = attemptId,
            workerId  = worker1
        });
        acceptResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Worker1 commits SUCCEEDED result — releases the slot
        var resultResp = await client.PostAsJsonAsync($"/jobs/{runId}/result", new
        {
            attemptId    = attemptId,
            terminalState = "Succeeded"
        });
        resultResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var resultBody = await ParseJsonAsync(resultResp);
        resultBody.GetProperty("slotReleased").GetBoolean().Should().BeTrue();

        // Now create a second job and verify Worker2 can pull it
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

        var pull3Resp = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId             = worker2,
            runtimeType          = "browser",
            capabilityNames      = Array.Empty<string>(),
            interactiveSessionId = session
        });
        pull3Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pull3Body   = await ParseJsonAsync(pull3Resp);
        var pull3Result = pull3Body.GetProperty("result").GetString();

        // Worker2 should now receive the new job (session slot is free)
        pull3Result.Should().Be("JobOffered",
            "After Worker1 releases the slot, Worker2 must be able to pull a new job on the same session.");
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
