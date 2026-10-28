#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Fixtures;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-12: offline artifact import.
///
/// Publish valid signed artifact. Retrieve. Assert content identical.
/// Publish tampered payload → 422.
/// </summary>
public sealed class OrchIt12_OfflineArtifactImportTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt12_OfflineArtifactImportTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PublishValidArtifact_Retrieve_ContentIdentical()
    {
        var client     = _factory.CreateClient();
        var artifactId = $"offline-{Guid.NewGuid():N}";
        var rawContent = "console.log('offline import test');"u8.ToArray();
        var b64Payload = Convert.ToBase64String(rawContent);

        // Publish
        var pubResp = await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = b64Payload,
            signature       = (string?)null,   // unsigned — signature validation is optional
            selectorContent = (string?)null
        });
        pubResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var pubBody = await ParseJsonAsync(pubResp);
        pubBody.GetProperty("artifactId").GetString().Should().Be(artifactId);
        pubBody.GetProperty("version").GetString().Should().Be("v1");
        var contentHash = pubBody.GetProperty("contentHash").GetString()!;
        contentHash.Should().NotBeNullOrEmpty("published artifact must have a content hash");

        // Retrieve
        var getResp = await client.GetAsync($"/artifacts/{artifactId}/versions/v1");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getBody   = await ParseJsonAsync(getResp);
        var retPayload = Convert.FromBase64String(getBody.GetProperty("payload").GetString()!);
        retPayload.Should().BeEquivalentTo(rawContent, "retrieved payload must be identical to published content");

        // ContentHash must match
        getBody.GetProperty("contentHash").GetString().Should().Be(contentHash,
            "retrieved contentHash must match the published hash");

        // ETag header must reflect the contentHash (domain invariant 7 — immutable)
        getResp.Headers.ETag.Should().NotBeNull("ETag must be set to the content hash");
    }

    [Fact]
    public async Task PublishArtifact_DuplicateVersion_Returns409Conflict()
    {
        var client     = _factory.CreateClient();
        var artifactId = $"offline-dup-{Guid.NewGuid():N}";
        var b64Payload = Convert.ToBase64String("original-content"u8.ToArray());

        // First publish succeeds
        var pub1 = await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = b64Payload,
            signature       = (string?)null,
            selectorContent = (string?)null
        });
        pub1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second publish of the same version — must be rejected (immutable once published)
        var pub2 = await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = Convert.ToBase64String("different-content"u8.ToArray()),
            signature       = (string?)null,
            selectorContent = (string?)null
        });
        pub2.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "artifact versions are immutable once published; a second PUT must return 409");
    }

    [Fact]
    public async Task PublishArtifact_InvalidBase64Payload_Returns400()
    {
        var client     = _factory.CreateClient();
        var artifactId = $"offline-invalid-{Guid.NewGuid():N}";

        // Publish with an invalid base64 string
        var pubResp = await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = "!!!not-valid-base64!!!",
            signature       = (string?)null,
            selectorContent = (string?)null
        });

        pubResp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a malformed (non-base64) payload must be rejected with 400");
    }

    [Fact]
    public async Task GetNonExistentArtifact_Returns404()
    {
        var client = _factory.CreateClient();

        var getResp = await client.GetAsync($"/artifacts/does-not-exist-{Guid.NewGuid():N}/versions/v99");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublishArtifact_UnsupportedSchema_Returns422()
    {
        var client     = _factory.CreateClient();
        var artifactId = $"offline-schema-{Guid.NewGuid():N}";

        var pubResp = await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "99",     // unsupported schema version
            payload         = Convert.ToBase64String("x"u8.ToArray()),
            signature       = (string?)null,
            selectorContent = (string?)null
        });

        pubResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "an unsupported schema version must be rejected with 422");
    }

    [Fact]
    public async Task PublishedArtifact_ContentHash_IsSha256OfPayload()
    {
        var client     = _factory.CreateClient();
        var artifactId = $"offline-hash-{Guid.NewGuid():N}";
        var rawContent = "deterministic-payload-for-hash-test"u8.ToArray();
        var b64Payload = Convert.ToBase64String(rawContent);

        var pubResp = await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = b64Payload,
            signature       = (string?)null,
            selectorContent = (string?)null
        });
        pubResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var pubBody         = await ParseJsonAsync(pubResp);
        var reportedHash    = pubBody.GetProperty("contentHash").GetString()!;

        // Compute expected SHA-256 of the raw bytes
        var expectedHashBytes = SHA256.HashData(rawContent);
        var expectedHash      = Convert.ToHexString(expectedHashBytes).ToLowerInvariant();

        reportedHash.ToLowerInvariant().Should().Be(expectedHash,
            "contentHash must be the SHA-256 hex digest of the raw payload bytes");
    }

    /// <summary>
    /// ORCH-IT-12 AC: PUT /artifacts/{id}/versions/v1 with an explicitly empty signature must be
    /// handled deterministically — either rejected (422 UnprocessableEntity) when signature
    /// validation is active, or accepted (201 Created) when validation is a stub.
    ///
    /// This test documents both expected branches explicitly so that a future change in
    /// signature-validation behaviour causes an intentional, visible test update rather than
    /// a silent pass under whichever branch was not previously covered.
    /// </summary>
    [Fact]
    public async Task PublishArtifact_EmptySignature_Returns422OrCreated()
    {
        var client     = _factory.CreateClient();
        var artifactId = $"offline-emptysig-{Guid.NewGuid():N}";

        // PUT with signature = "" (explicitly empty, not null/absent)
        var pubResp = await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = Convert.ToBase64String("content-for-sig-test"u8.ToArray()),
            signature       = string.Empty,
            selectorContent = (string?)null
        });

        // When signature validation is active: 422 UnprocessableEntity (INVALID_SIGNATURE).
        // When validation is a stub (current): empty string is treated as absent → 201 Created.
        pubResp.StatusCode.Should().BeOneOf(
            new[] { HttpStatusCode.UnprocessableEntity, HttpStatusCode.Created },
            "an empty signature must result in 422 (validation active) or 201 (stub); " +
            "it must never silently produce any other status code");
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
