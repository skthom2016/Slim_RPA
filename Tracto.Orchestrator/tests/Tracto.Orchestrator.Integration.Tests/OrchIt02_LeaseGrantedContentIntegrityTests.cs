#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Fixtures;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-02: leaseGranted content integrity.
///
/// Accept lease. Verify payload has {artifactHash, attemptId, inputRefs}.
/// Assert no plaintext secret.
/// </summary>
public sealed class OrchIt02_LeaseGrantedContentIntegrityTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt02_LeaseGrantedContentIntegrityTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AcceptLease_Response_Contains_ArtifactHash_AttemptId_InputRefs_NoSecrets()
    {
        var client = _factory.AuthenticatedClient();
        var workerId   = $"worker-{Guid.NewGuid():N}";
        var artifactId = $"script-orch-it02-{Guid.NewGuid():N}";

        // Register worker
        var regResp = await client.PostAsJsonAsync("/workers/register", new
        {
            workerId,
            runtimeType             = "dotnet",
            actionNames             = new[] { "Run" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        });
        regResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Publish artifact
        var payloadBytes = "print('hello')"u8.ToArray();
        var pubResp = await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = Convert.ToBase64String(payloadBytes),
            signature       = (string?)null,
            selectorContent = (string?)null
        });
        pubResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Retrieve published artifact to know its hash
        var getResp  = await client.GetAsync($"/artifacts/{artifactId}/versions/v1");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var getBody  = await ParseJsonAsync(getResp);
        var expectedHash = getBody.GetProperty("contentHash").GetString()!;

        // Create job with inputRefs and secretRefs (opaque refs — no actual secrets)
        var jobResp = await client.PostAsJsonAsync("/jobs", new
        {
            scriptArtifactId = artifactId,
            scriptVersion    = "v1",
            schemaVersion    = "1",
            inputRefs        = new[] { "blob://input-ref-1" },
            secretRefs       = new[] { "ref:vault/secret-ref-1" },
            triggerType      = "Manual"
        });
        jobResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var jobBody = await ParseJsonAsync(jobResp);
        var runId   = Guid.Parse(jobBody.GetProperty("runId").GetString()!);

        // Pull the job
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

        // Accept the lease — this is the leaseGranted response we validate
        var acceptResp = await client.PostAsJsonAsync($"/jobs/{runId}/lease/accept", new
        {
            attemptId,
            workerId
        });
        acceptResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var leaseBody = await ParseJsonAsync(acceptResp);

        // Assert: artifactHash present and matches
        leaseBody.TryGetProperty("artifactHash", out var hashProp).Should().BeTrue("artifactHash must be present in lease payload");
        hashProp.GetString().Should().Be(expectedHash, "artifactHash must equal the hash of the published artifact");

        // Assert: attemptId echoed back
        leaseBody.TryGetProperty("attemptId", out var attemptProp).Should().BeTrue("attemptId must be echoed in lease payload");
        Guid.Parse(attemptProp.GetString()!).Should().Be(attemptId);

        // Assert: inputRefs present
        leaseBody.TryGetProperty("inputRefs", out var inputRefsProp).Should().BeTrue("inputRefs must be present in lease payload");
        inputRefsProp.GetArrayLength().Should().Be(1);
        inputRefsProp[0].GetString().Should().Be("blob://input-ref-1");

        // Assert: secretRefs present (opaque ref, not resolved value)
        leaseBody.TryGetProperty("secretRefs", out var secretRefsProp).Should().BeTrue("secretRefs must be present in lease payload (as opaque refs)");
        secretRefsProp.GetArrayLength().Should().Be(1);

        // Assert: no plaintext secret values — secretRefs contains only the opaque ref string, not actual secret material
        // Domain invariant 4: no resolved secret values are ever included
        var secretRef = secretRefsProp[0].GetString()!;
        secretRef.Should().Be("ref:vault/secret-ref-1", "only the opaque reference is stored/returned, never the resolved value");

        // Assert: the raw response JSON does not contain "Bearer", "Password", or similar sensitive patterns
        var rawJson = await acceptResp.Content.ReadAsStringAsync();
        rawJson.Should().NotContain("password", "plaintext secrets must never appear in lease payload");
        rawJson.Should().NotContain("apikey",   "plaintext secrets must never appear in lease payload");
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
