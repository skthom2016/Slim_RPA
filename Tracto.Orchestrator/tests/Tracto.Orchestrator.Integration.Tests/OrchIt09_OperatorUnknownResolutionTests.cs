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
/// ORCH-IT-09a: InvestigatedNoRerun leaves the original run UNKNOWN.
///
/// Isolated in its own class to prevent DB state leakage from OrchIt09b (ApprovedNewRun
/// creates a new QUEUED run that could interfere with subsequent pulls).
/// </summary>
public sealed class OrchIt09a_InvestigatedNoRerunTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt09a_InvestigatedNoRerunTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InvestigatedNoRerun_OriginalRunRemainsUnknown()
    {
        var client = _factory.AuthenticatedClient();
        var runId  = await CreateUnknownRunAsync(client);

        // Act — POST InvestigatedNoRerun
        var resolveResp = await client.PostAsJsonAsync($"/admin/jobs/{runId}/resolve", new
        {
            type          = "INVESTIGATED_NO_RERUN",
            justification = "Confirmed external API call failed; no retry needed.",
            operatorId    = "operator@tracto.io"
        });
        resolveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolveBody = await ParseJsonAsync(resolveResp);
        resolveBody.TryGetProperty("resolutionId", out _).Should().BeTrue("resolutionId must be present");
        resolveBody.GetProperty("runId").GetString().Should().Be(runId.ToString());

        // NewRunId must be null for InvestigatedNoRerun
        if (resolveBody.TryGetProperty("newRunId", out var newRunIdProp))
        {
            newRunIdProp.ValueKind.Should().Be(JsonValueKind.Null,
                "InvestigatedNoRerun must not create a new run");
        }

        // Assert original run STILL UNKNOWN (invariant: UNKNOWN runs are never auto-transitioned)
        var detailResp = await client.GetAsync($"/admin/jobs/{runId}/unknown-detail");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailBody = await ParseJsonAsync(detailResp);
        detailBody.GetProperty("state").GetString().Should().Be("UNKNOWN",
            "InvestigatedNoRerun must not change the original run's state from UNKNOWN");
        detailBody.GetProperty("needsAttention").GetBoolean().Should().BeTrue();

        // AC: Assert ResolutionRecord was written to DB
        // This guards against a handler that returns a fabricated resolutionId without persisting.
        var resolutionId = Guid.Parse(resolveBody.GetProperty("resolutionId").GetString()!);
        using var dbScope = _factory.Services.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var savedRecord = await db.ResolutionRecords.FirstOrDefaultAsync(r => r.Id == resolutionId);
        savedRecord.Should().NotBeNull(
            "AC requires a ResolutionRecord row to be persisted in the DB for InvestigatedNoRerun");
        savedRecord!.RunId.Should().Be(runId,
            "the persisted ResolutionRecord must reference the original UNKNOWN run");
    }

    // ── Helper — create a run, put it in UNKNOWN via reconcile ───────────────────

    private async Task<Guid> CreateUnknownRunAsync(HttpClient client)
    {
        var workerId   = $"worker-{Guid.NewGuid():N}";
        var artifactId = $"script-orch-it09a-{Guid.NewGuid():N}";

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

        var pullResp = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId,
            runtimeType     = "dotnet",
            capabilityNames = Array.Empty<string>()
        });
        var pullBody  = await ParseJsonAsync(pullResp);
        var attemptId = Guid.Parse(pullBody.GetProperty("attemptId").GetString()!);

        await client.PostAsJsonAsync($"/jobs/{runId}/lease/accept", new { attemptId, workerId });

        // Reconcile with empty journal → UNKNOWN
        var reconResp = await client.PostAsJsonAsync($"/jobs/{runId}/reconcile", new
        {
            attemptId,
            journalEntries = Array.Empty<object>()
        });
        reconResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ParseJsonAsync(reconResp)).GetProperty("reconciledState").GetString().Should().Be("UNKNOWN");

        return runId;
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}

/// <summary>
/// ORCH-IT-09b: ApprovedNewRun creates a new RunId; original run stays UNKNOWN.
///
/// Isolated in its own class to prevent DB state leakage from OrchIt09a.
/// </summary>
public sealed class OrchIt09b_ApprovedNewRunTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt09b_ApprovedNewRunTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ApprovedNewRun_CreatesNewRunId_OriginalStillUnknown()
    {
        var client = _factory.AuthenticatedClient();
        var runId  = await CreateUnknownRunAsync(client);

        // Act — POST ApprovedNewRun
        var resolveResp = await client.PostAsJsonAsync($"/admin/jobs/{runId}/resolve", new
        {
            type          = "APPROVED_NEW_RUN",
            justification = "Operator approved retry after investigating the UNKNOWN run.",
            operatorId    = "operator@tracto.io"
        });
        resolveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resolveBody = await ParseJsonAsync(resolveResp);
        resolveBody.TryGetProperty("resolutionId", out _).Should().BeTrue();
        resolveBody.GetProperty("runId").GetString().Should().Be(runId.ToString());

        // NewRunId must be non-null for ApprovedNewRun
        resolveBody.TryGetProperty("newRunId", out var newRunIdProp).Should().BeTrue();
        newRunIdProp.ValueKind.Should().NotBe(JsonValueKind.Null, "ApprovedNewRun must create a new run");
        var newRunId = Guid.Parse(newRunIdProp.GetString()!);
        newRunId.Should().NotBe(Guid.Empty);
        newRunId.Should().NotBe(runId, "new run must have a different RunId from the original");

        // Original run must still be UNKNOWN
        var detailResp = await client.GetAsync($"/admin/jobs/{runId}/unknown-detail");
        detailResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailBody = await ParseJsonAsync(detailResp);
        detailBody.GetProperty("state").GetString().Should().Be("UNKNOWN",
            "ApprovedNewRun must not transition the original UNKNOWN run's state");
        detailBody.GetProperty("needsAttention").GetBoolean().Should().BeTrue();

        // New run must exist and be in QUEUED or CREATED state (not terminal)
        var newRunResp = await client.GetAsync($"/jobs/{newRunId}");
        newRunResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var newRunBody = await ParseJsonAsync(newRunResp);
        var newState   = newRunBody.GetProperty("state").GetString()!;
        newState.Should().BeOneOf("CREATED", "QUEUED",
            "the new run approved for replay must start in a non-terminal state");

        // AC: Assert ResolutionRecord was written to DB
        // This guards against a handler that returns a fabricated resolutionId without persisting.
        var resolutionId = Guid.Parse(resolveBody.GetProperty("resolutionId").GetString()!);
        using var dbScope = _factory.Services.CreateScope();
        var db = dbScope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var savedRecord = await db.ResolutionRecords.FirstOrDefaultAsync(r => r.Id == resolutionId);
        savedRecord.Should().NotBeNull(
            "AC requires a ResolutionRecord row to be persisted in the DB for ApprovedNewRun");
        savedRecord!.RunId.Should().Be(runId,
            "the persisted ResolutionRecord must reference the original UNKNOWN run");
        savedRecord.NewRunId.Should().Be(newRunId,
            "the persisted ResolutionRecord must record the newly approved Run's Id");
    }

    // ── Helper — create a run, put it in UNKNOWN via reconcile ───────────────────

    private async Task<Guid> CreateUnknownRunAsync(HttpClient client)
    {
        var workerId   = $"worker-{Guid.NewGuid():N}";
        var artifactId = $"script-orch-it09b-{Guid.NewGuid():N}";

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

        var pullResp = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId,
            runtimeType     = "dotnet",
            capabilityNames = Array.Empty<string>()
        });
        var pullBody  = await ParseJsonAsync(pullResp);
        var attemptId = Guid.Parse(pullBody.GetProperty("attemptId").GetString()!);

        await client.PostAsJsonAsync($"/jobs/{runId}/lease/accept", new { attemptId, workerId });

        // Reconcile with empty journal → UNKNOWN
        var reconResp = await client.PostAsJsonAsync($"/jobs/{runId}/reconcile", new
        {
            attemptId,
            journalEntries = Array.Empty<object>()
        });
        reconResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ParseJsonAsync(reconResp)).GetProperty("reconciledState").GetString().Should().Be("UNKNOWN");

        return runId;
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
