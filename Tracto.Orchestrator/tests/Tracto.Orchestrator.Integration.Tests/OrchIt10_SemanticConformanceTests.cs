#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Fixtures;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-10: semantic conformance.
///
/// Assert all API endpoints match expected message shapes (contract test using real HTTP +
/// typed deserialization). Tests that each endpoint returns the documented response fields.
/// </summary>
public sealed class OrchIt10_SemanticConformanceTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt10_SemanticConformanceTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    // ── GET /health ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetHealth_ShapeConforms()
    {
        var client   = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.TryGetProperty("status",      out _).Should().BeTrue("status field required");
        body.TryGetProperty("dbConnected", out _).Should().BeTrue("dbConnected field required");
        body.TryGetProperty("queueDepth",  out _).Should().BeTrue("queueDepth field required");
        body.TryGetProperty("mode",        out _).Should().BeTrue("mode field required");

        body.GetProperty("status").ValueKind.Should().Be(JsonValueKind.String);
        body.GetProperty("dbConnected").ValueKind.Should().Be(JsonValueKind.True);
        body.GetProperty("queueDepth").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    // ── POST /workers/register ─────────────────────────────────────────────────

    [Fact]
    public async Task PostWorkersRegister_ShapeConforms()
    {
        var client   = _factory.AuthenticatedClient();
        var workerId = $"conformance-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync("/workers/register", new
        {
            workerId,
            runtimeType             = "dotnet",
            actionNames             = new[] { "ClickAction" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.TryGetProperty("workerId", out var wid).Should().BeTrue("workerId field required");
        wid.GetString().Should().Be(workerId);
    }

    // ── POST /jobs ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostJobs_ShapeConforms()
    {
        var client     = _factory.AuthenticatedClient();
        var artifactId = $"conf-art-{Guid.NewGuid():N}";

        await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = Convert.ToBase64String("s"u8.ToArray()),
            signature       = (string?)null,
            selectorContent = (string?)null
        });

        var response = await client.PostAsJsonAsync("/jobs", new
        {
            scriptArtifactId = artifactId,
            scriptVersion    = "v1",
            schemaVersion    = "1",
            inputRefs        = Array.Empty<string>(),
            secretRefs       = Array.Empty<string>(),
            triggerType      = "Manual"
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.TryGetProperty("runId", out _).Should().BeTrue("runId field required");
        body.TryGetProperty("state", out _).Should().BeTrue("state field required");
        body.GetProperty("runId").ValueKind.Should().Be(JsonValueKind.String);
        Guid.TryParse(body.GetProperty("runId").GetString(), out _).Should().BeTrue("runId must be a GUID");
    }

    // ── POST /jobs/pull — NoPendingJob shape ───────────────────────────────────

    [Fact]
    public async Task PostJobsPull_NoPendingJob_ShapeConforms()
    {
        var client   = _factory.AuthenticatedClient();
        var workerId = $"conf-pull-{Guid.NewGuid():N}";

        await client.PostAsJsonAsync("/workers/register", new
        {
            workerId,
            runtimeType             = "dotnet",
            actionNames             = new[] { "Noop" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        });

        var response = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId,
            runtimeType     = "dotnet",
            capabilityNames = Array.Empty<string>()
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.TryGetProperty("result", out var resultProp).Should().BeTrue("result field required");
        resultProp.GetString().Should().Be("NoPendingJob");
    }

    // ── PUT /artifacts/{id}/versions/{version} ─────────────────────────────────

    [Fact]
    public async Task PutArtifact_ShapeConforms()
    {
        var client     = _factory.CreateClient();
        var artifactId = $"conf-art2-{Guid.NewGuid():N}";
        var payload    = Convert.ToBase64String("hello"u8.ToArray());

        var response = await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v2", new
        {
            schemaVersion   = "1",
            payload,
            signature       = (string?)null,
            selectorContent = (string?)null
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        body.TryGetProperty("artifactId",   out _).Should().BeTrue("artifactId field required");
        body.TryGetProperty("version",      out _).Should().BeTrue("version field required");
        body.TryGetProperty("contentHash",  out _).Should().BeTrue("contentHash field required");
        body.GetProperty("artifactId").GetString().Should().Be(artifactId);
        body.GetProperty("version").GetString().Should().Be("v2");
    }

    // ── GET /artifacts/{id}/versions/{version} ─────────────────────────────────

    [Fact]
    public async Task GetArtifact_ShapeConforms()
    {
        var client     = _factory.CreateClient();
        var artifactId = $"conf-art3-{Guid.NewGuid():N}";
        var rawPayload = "artifact-content"u8.ToArray();
        var b64Payload = Convert.ToBase64String(rawPayload);

        await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v3", new
        {
            schemaVersion   = "1",
            payload         = b64Payload,
            signature       = (string?)null,
            selectorContent = (string?)null
        });

        var response = await client.GetAsync($"/artifacts/{artifactId}/versions/v3");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        body.TryGetProperty("payload",     out _).Should().BeTrue("payload field required");
        body.TryGetProperty("contentHash", out _).Should().BeTrue("contentHash field required");

        // Payload must round-trip correctly
        var returnedPayload = Convert.FromBase64String(body.GetProperty("payload").GetString()!);
        returnedPayload.Should().BeEquivalentTo(rawPayload, "payload must round-trip unchanged");

        // ETag header must be set to the content hash
        response.Headers.ETag.Should().NotBeNull("ETag header must be set to the content hash");
    }

    // ── POST /continuation-mappings ───────────────────────────────────────────

    [Fact]
    public async Task PostContinuationMappings_ShapeConforms()
    {
        var client     = _factory.AuthenticatedClient();
        var artifactId = $"conf-cont-{Guid.NewGuid():N}";

        await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = Convert.ToBase64String("c"u8.ToArray()),
            signature       = (string?)null,
            selectorContent = (string?)null
        });

        var response = await client.PostAsJsonAsync("/continuation-mappings", new
        {
            triggerRunId          = (Guid?)null,
            triggerState          = "Succeeded",
            targetArtifactId      = artifactId,
            targetArtifactVersion = "v1"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await ParseJsonAsync(response);
        body.TryGetProperty("mappingId",             out _).Should().BeTrue("mappingId required");
        body.TryGetProperty("triggerState",           out _).Should().BeTrue("triggerState required");
        body.TryGetProperty("targetArtifactId",       out _).Should().BeTrue("targetArtifactId required");
        body.TryGetProperty("targetArtifactVersion",  out _).Should().BeTrue("targetArtifactVersion required");
        body.TryGetProperty("createdAt",              out _).Should().BeTrue("createdAt required");
        body.TryGetProperty("version",                out _).Should().BeTrue("version required");
    }

    // ── GET /admin/jobs ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetAdminJobs_ShapeConforms()
    {
        var client   = _factory.AuthenticatedClient();
        var response = await client.GetAsync("/admin/jobs");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ParseJsonAsync(response);
        // AdminJobPage should have items and pagination fields
        body.TryGetProperty("items", out _).Should().BeTrue("items field required on admin job listing");
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
