#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Shared;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-10: semantic conformance.
///
/// AC:
///   Assert all API endpoints match expected message shapes (contract test using
///   real HTTP + typed deserialization).
/// </summary>
public sealed class OrchIt10SemanticConformance
    : IClassFixture<OrchIt10SemanticConformance.Factory>
{
    public sealed class Factory : OrchestratorTestFactory { }

    private readonly HttpClient _client;

    public OrchIt10SemanticConformance(Factory factory)
    {
        _client = factory.AuthenticatedClient();
    }

    // ── Health ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHealth_ResponseShape_MatchesContract()
    {
        var resp = await _client.GetAsync("/health");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("status",      out _).Should().BeTrue("status field required");
        body.TryGetProperty("dbConnected", out _).Should().BeTrue("dbConnected field required");
        body.TryGetProperty("queueDepth",  out _).Should().BeTrue("queueDepth field required");
        body.TryGetProperty("mode",        out _).Should().BeTrue("mode field required");
        body.GetProperty("status").GetString().Should().Be("healthy");
    }

    // ── Worker registration ───────────────────────────────────────────────────

    [Fact]
    public async Task PostWorkersRegister_ResponseShape_MatchesContract()
    {
        var resp = await _client.PostAsJsonAsync("/workers/register", new
        {
            workerId                = "worker-conformance-1",
            runtimeType             = "dotnet",
            actionNames             = new[] { "act.RunScript" },
            capabilityDigest        = "abc123",
            absenceThresholdSeconds = 60
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("workerId", out var wid).Should().BeTrue("workerId required");
        wid.GetString().Should().Be("worker-conformance-1");
    }

    // ── Job creation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PostJobs_ResponseShape_MatchesContract()
    {
        await ApiHelpers.PublishArtifactAsync(_client, "art-conf-10", "1.0");

        var resp = await _client.PostAsJsonAsync("/jobs", new
        {
            scriptArtifactId = "art-conf-10",
            scriptVersion    = "1.0",
            schemaVersion    = "1",
            inputRefs        = Array.Empty<string>(),
            secretRefs       = Array.Empty<string>(),
            deadline         = (DateTime?)null,
            eventId          = (string?)null,
            triggerType      = "Manual",
            businessJobId    = (string?)null
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("runId", out var runId).Should().BeTrue("runId required");
        body.TryGetProperty("state", out var state).Should().BeTrue("state required");
        runId.GetGuid().Should().NotBeEmpty();
        state.GetString().Should().Be("QUEUED");
    }

    // ── Job pull ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostJobsPull_NoPendingJob_ResponseShape_MatchesContract()
    {
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-conf-10", "dotnet");

        var resp = await _client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId             = "worker-conf-10",
            runtimeType          = "dotnet",
            capabilityNames      = Array.Empty<string>(),
            interactiveSessionId = (string?)null
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("result", out var result).Should().BeTrue("result field required");
        result.GetString().Should().Be("NoPendingJob");
    }

    [Fact]
    public async Task PostJobsPull_JobOffered_ResponseShape_MatchesContract()
    {
        await ApiHelpers.PublishArtifactAsync(_client, "art-conf-10b", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-conf-10b", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-conf-10b", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-conf-10b", "dotnet");
        pullJson.GetProperty("result").GetString().Should().Be("JobOffered");
        pullJson.TryGetProperty("runId",           out _).Should().BeTrue("runId required");
        pullJson.TryGetProperty("attemptId",       out _).Should().BeTrue("attemptId required");
        pullJson.TryGetProperty("leaseId",         out _).Should().BeTrue("leaseId required");
        pullJson.TryGetProperty("artifactId",      out _).Should().BeTrue("artifactId required");
        pullJson.TryGetProperty("artifactVersion", out _).Should().BeTrue("artifactVersion required");
        pullJson.TryGetProperty("artifactHash",    out _).Should().BeTrue("artifactHash required");
        pullJson.TryGetProperty("inputRefs",       out _).Should().BeTrue("inputRefs required");
        pullJson.TryGetProperty("secretRefs",      out _).Should().BeTrue("secretRefs required");
    }

    // ── Accept lease ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PostAcceptLease_ResponseShape_MatchesContract()
    {
        await ApiHelpers.PublishArtifactAsync(_client, "art-conf-10c", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-conf-10c", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-conf-10c", "1.0");
        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-conf-10c", "dotnet");
        var attemptId   = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        var leaseJson = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-conf-10c");
        leaseJson.TryGetProperty("runId",          out _).Should().BeTrue("runId required");
        leaseJson.TryGetProperty("attemptId",      out _).Should().BeTrue("attemptId required");
        leaseJson.TryGetProperty("artifactId",     out _).Should().BeTrue("artifactId required");
        leaseJson.TryGetProperty("artifactVersion",out _).Should().BeTrue("artifactVersion required");
        leaseJson.TryGetProperty("artifactHash",   out _).Should().BeTrue("artifactHash required");
        leaseJson.TryGetProperty("inputRefs",      out _).Should().BeTrue("inputRefs required");
        leaseJson.TryGetProperty("secretRefs",     out _).Should().BeTrue("secretRefs required");
    }

    // ── Heartbeat ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostHeartbeat_ResponseShape_MatchesContract()
    {
        await ApiHelpers.PublishArtifactAsync(_client, "art-conf-10d", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-conf-10d", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-conf-10d", "1.0");
        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-conf-10d", "dotnet");
        var attemptId   = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();
        var leaseJson   = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-conf-10d");
        var runningAttemptId = leaseJson.GetProperty("attemptId").GetGuid();

        var hbJson = await ApiHelpers.SendHeartbeatAsync(
            _client, offeredRunId, runningAttemptId, "worker-conf-10d", seqNo: 1);
        hbJson.TryGetProperty("cancellationRequested", out var cr).Should().BeTrue("cancellationRequested required");
        cr.ValueKind.Should().Be(JsonValueKind.False);
    }

    // ── Final result ──────────────────────────────────────────────────────────

    [Fact]
    public async Task PostFinalResult_ResponseShape_MatchesContract()
    {
        await ApiHelpers.PublishArtifactAsync(_client, "art-conf-10e", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-conf-10e", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-conf-10e", "1.0");
        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-conf-10e", "dotnet");
        var attemptId   = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();
        var leaseJson   = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-conf-10e");
        var runningAttemptId = leaseJson.GetProperty("attemptId").GetGuid();

        var resultJson = await ApiHelpers.CommitResultAsync(
            _client, offeredRunId, runningAttemptId, "Succeeded");
        resultJson.TryGetProperty("runId",         out _).Should().BeTrue("runId required");
        resultJson.TryGetProperty("attemptId",     out _).Should().BeTrue("attemptId required");
        resultJson.TryGetProperty("terminalState", out var ts).Should().BeTrue("terminalState required");
        resultJson.TryGetProperty("slotReleased",  out _).Should().BeTrue("slotReleased required");
        ts.GetString().Should().Be("SUCCEEDED");
    }

    // ── Reconcile ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostReconcile_ResponseShape_MatchesContract()
    {
        await ApiHelpers.PublishArtifactAsync(_client, "art-conf-10f", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-conf-10f", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-conf-10f", "1.0");
        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-conf-10f", "dotnet");
        var attemptId   = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();
        await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-conf-10f");

        var reconResp = await ApiHelpers.ReconcileAsync(_client, offeredRunId, attemptId, []);
        reconResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var reconJson = await reconResp.Content.ReadFromJsonAsync<JsonElement>();
        reconJson.TryGetProperty("runId",           out _).Should().BeTrue("runId required");
        reconJson.TryGetProperty("attemptId",       out _).Should().BeTrue("attemptId required");
        reconJson.TryGetProperty("reconciledState", out _).Should().BeTrue("reconciledState required");
    }

    // ── Invalid inputs return ProblemDetails ──────────────────────────────────

    [Fact]
    public async Task PostJobs_UnsupportedSchemaVersion_Returns422WithProblemDetails()
    {
        await ApiHelpers.PublishArtifactAsync(_client, "art-conf-10g", "1.0");

        var resp = await _client.PostAsJsonAsync("/jobs", new
        {
            scriptArtifactId = "art-conf-10g",
            scriptVersion    = "1.0",
            schemaVersion    = "99",          // unsupported
            inputRefs        = Array.Empty<string>(),
            secretRefs       = Array.Empty<string>(),
            triggerType      = "Manual"
        });
        ((int)resp.StatusCode).Should().Be(422);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("title",  out _).Should().BeTrue("ProblemDetails title required");
        body.TryGetProperty("detail", out _).Should().BeTrue("ProblemDetails detail required");
        body.TryGetProperty("status", out _).Should().BeTrue("ProblemDetails status required");
    }
}
