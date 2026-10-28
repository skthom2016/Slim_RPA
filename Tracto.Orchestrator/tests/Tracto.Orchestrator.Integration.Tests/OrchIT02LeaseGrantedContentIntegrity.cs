#nullable enable
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Shared;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-02: leaseGranted content integrity.
///
/// AC:
///   Accept lease. Verify payload has {artifactHash, attemptId, inputRefs}.
///   Assert no plaintext secret.
/// </summary>
public sealed class OrchIt02LeaseGrantedContentIntegrity
    : IClassFixture<OrchIt02LeaseGrantedContentIntegrity.Factory>
{
    public sealed class Factory : OrchestratorTestFactory { }

    private readonly HttpClient _client;

    public OrchIt02LeaseGrantedContentIntegrity(Factory factory)
    {
        _client = factory.AuthenticatedClient();
    }

    [Fact]
    public async Task AcceptLease_ResponseContains_ArtifactHash_AttemptId_InputRefs()
    {
        // Arrange.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc02", "1.0",
            payload: "oc02-script-payload", signature: "sig-ok");

        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc02", "dotnet");

        var jobJson = await ApiHelpers.CreateJobAsync(
            _client, "art-oc02", "1.0",
            inputRefs: ["ref://input/1", "ref://input/2"],
            secretRefs: ["ref:vault/secret-key"]);
        var runId = jobJson.GetProperty("runId").GetGuid();

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc02", "dotnet");
        pullJson.GetProperty("result").GetString().Should().Be("JobOffered");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        // Act — accept the lease.
        var leaseJson = await ApiHelpers.AcceptLeaseAsync(
            _client, offeredRunId, attemptId, "worker-oc02");

        // Assert: required fields are present.
        leaseJson.TryGetProperty("artifactHash", out var hashProp).Should().BeTrue("artifactHash must be present");
        hashProp.GetString().Should().NotBeNullOrWhiteSpace("artifactHash must be non-empty");

        leaseJson.TryGetProperty("attemptId", out var attemptProp).Should().BeTrue("attemptId must be present");
        attemptProp.GetGuid().Should().NotBeEmpty("attemptId must be a non-empty GUID");

        leaseJson.TryGetProperty("inputRefs", out var inputRefsProp).Should().BeTrue("inputRefs must be present");
        var inputRefsArr = inputRefsProp.EnumerateArray().Select(x => x.GetString()!).ToArray();
        inputRefsArr.Should().Contain("ref://input/1");
        inputRefsArr.Should().Contain("ref://input/2");

        leaseJson.TryGetProperty("secretRefs", out var secretRefsProp).Should().BeTrue("secretRefs must be present");
        var secretRefsArr = secretRefsProp.EnumerateArray().Select(x => x.GetString()!).ToArray();
        secretRefsArr.Should().Contain("ref:vault/secret-key");
    }

    [Fact]
    public async Task AcceptLease_ResponseDoesNotContain_PlaintextSecretValues()
    {
        // Arrange.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc02b", "1.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc02b", "dotnet");
        await ApiHelpers.CreateJobAsync(
            _client, "art-oc02b", "1.0",
            secretRefs: ["ref:vault/db-password", "ref:vault/api-key"]);

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc02b", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        // Act.
        var leaseJson = await ApiHelpers.AcceptLeaseAsync(
            _client, offeredRunId, attemptId, "worker-oc02b");

        // Assert: secretRefs contains only opaque references, not plaintext values.
        // Domain invariant 4: no plaintext secrets stored or returned.
        var rawJson = leaseJson.GetRawText();
        rawJson.Should().NotContain("password=", because: "no plaintext secret values may appear");
        rawJson.Should().NotContain("api_key=", because: "no plaintext secret values may appear");

        // The secret refs are opaque references, not resolved values.
        var secretRefsArr = leaseJson.GetProperty("secretRefs")
            .EnumerateArray().Select(x => x.GetString()!).ToArray();
        // Each value should be an opaque reference (starts with "vault://"), not a raw secret.
        foreach (var secretRef in secretRefsArr)
        {
            secretRef.Should().StartWith("ref:",
                because: "secret refs are opaque references prefixed with 'ref:', not plaintext values");
        }
    }

    [Fact]
    public async Task AcceptLease_ArtifactHash_IsSha256OfPublishedContent()
    {
        // Arrange — publish with known content.
        var knownPayload = "deterministic-script-content";
        var payloadBytes = System.Text.Encoding.UTF8.GetBytes(knownPayload);
        var expectedHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(payloadBytes)).ToLowerInvariant();

        await ApiHelpers.PublishArtifactAsync(_client, "art-oc02c", "1.0",
            payload: knownPayload, signature: "sig-ok");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc02c", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc02c", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc02c", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        // Act.
        var leaseJson = await ApiHelpers.AcceptLeaseAsync(
            _client, offeredRunId, attemptId, "worker-oc02c");

        // Assert.
        leaseJson.GetProperty("artifactHash").GetString().Should().Be(expectedHash,
            because: "artifactHash must be the SHA-256 hex digest of the published payload");
    }
}
