using System.Security.Cryptography;
using FluentAssertions;
using Moq;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Artifacts;
using Tracto.Orchestrator.Domain.Artifacts;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Infrastructure.Clock;
using AppClock = Tracto.Orchestrator.Application.Abstractions.IClock;

namespace Tracto.Orchestrator.Unit.Tests.Artifacts;

/// <summary>
/// Unit tests for OC-11 Script and Artifact Registry.
///
/// ACs verified:
///   - Valid publish stores record + content and returns contentHash.
///   - Overwriting an existing version yields 409 VERSION_EXISTS.
///   - SchemaVersion "2" (non-"1") yields 422 UNSUPPORTED_SCHEMA diagnostic.
///   - Delete with an active LEASED/RUNNING run yields 409 ARTIFACT_IN_USE.
///   - GET returns payload and ContentHash that matches the ETag.
///   - SelectorContent round-trips opaque (unchanged after publish/get).
///   - Empty signature string is rejected as invalid (non-null but empty).
///   - Non-empty signature is accepted (stub: any non-empty = valid).
/// </summary>
public sealed class ArtifactRegistryTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FakeClock Clock() => new(FixedNow);

    private static string ExpectedHash(byte[] payload) =>
        Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    private static PublishArtifactCommand ValidPublishCommand(
        string? selectorContent = null,
        string? signature = null,
        string schemaVersion = "1") =>
        new(
            ArtifactId: "artifact-001",
            Version: "1.0.0",
            SchemaVersion: schemaVersion,
            Payload: [0x01, 0x02, 0x03],
            Signature: signature,
            SelectorContent: selectorContent);

    private static (PublishArtifactHandler handler, Mock<IArtifactRepository> repoMock)
        BuildPublishHandler(
            ArtifactRecord? existingRecord = null,
            AppClock? clock = null)
    {
        var repoMock = new Mock<IArtifactRepository>();

        repoMock
            .Setup(r => r.GetRecordAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingRecord);

        repoMock
            .Setup(r => r.AddAsync(
                It.IsAny<ArtifactRecord>(), It.IsAny<ArtifactContent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new PublishArtifactHandler(repoMock.Object, clock ?? Clock());
        return (handler, repoMock);
    }

    private static (GetArtifactHandler handler, Mock<IArtifactRepository> repoMock)
        BuildGetHandler(ArtifactRecord? record, ArtifactContent? content)
    {
        var repoMock = new Mock<IArtifactRepository>();

        repoMock
            .Setup(r => r.GetRecordAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        repoMock
            .Setup(r => r.GetContentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(content);

        var handler = new GetArtifactHandler(repoMock.Object);
        return (handler, repoMock);
    }

    private static (DeleteArtifactHandler handler, Mock<IArtifactRepository> repoMock)
        BuildDeleteHandler(ArtifactRecord? record, bool isReferencedByActiveRun = false)
    {
        var repoMock = new Mock<IArtifactRepository>();

        repoMock
            .Setup(r => r.GetRecordAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        repoMock
            .Setup(r => r.IsReferencedByActiveRunAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(isReferencedByActiveRun);

        repoMock
            .Setup(r => r.Update(It.IsAny<ArtifactRecord>()));

        var handler = new DeleteArtifactHandler(repoMock.Object);
        return (handler, repoMock);
    }

    private static ArtifactRecord MakeRecord(
        string artifactId = "artifact-001",
        string version = "1.0.0",
        string? selectorContent = null,
        bool isDeleted = false)
    {
        var payload = new byte[] { 0x01, 0x02, 0x03 };
        var hash = ExpectedHash(payload);
        var record = new ArtifactRecord(
            artifactId, version, "1", hash, FixedNow,
            signatureValid: true,
            selectorContent: selectorContent);
        if (isDeleted) record.SoftDelete();
        return record;
    }

    // ── AC: Valid publish → stored, contentHash returned ─────────────────────

    [Fact]
    public async Task Publish_ValidCommand_ReturnsSuccessWithContentHash()
    {
        // Arrange
        var (handler, repoMock) = BuildPublishHandler(existingRecord: null);
        var cmd = ValidPublishCommand();
        var expectedHash = ExpectedHash(cmd.Payload);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue(because: "a valid publish must succeed");
        result.Value.ArtifactId.Should().Be(cmd.ArtifactId);
        result.Value.Version.Should().Be(cmd.Version);
        result.Value.ContentHash.Should().Be(expectedHash,
            because: "returned hash must be the SHA-256 hex digest of the payload");
    }

    [Fact]
    public async Task Publish_ValidCommand_CallsAddAsync()
    {
        // Arrange
        var (handler, repoMock) = BuildPublishHandler(existingRecord: null);
        var cmd = ValidPublishCommand();

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        repoMock.Verify(
            r => r.AddAsync(
                It.Is<ArtifactRecord>(ar =>
                    ar.ArtifactId == cmd.ArtifactId &&
                    ar.Version == cmd.Version &&
                    ar.SchemaVersion == cmd.SchemaVersion),
                It.Is<ArtifactContent>(ac =>
                    ac.ArtifactId == cmd.ArtifactId &&
                    ac.Version == cmd.Version &&
                    ac.Payload.SequenceEqual(cmd.Payload)),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "AddAsync must be called exactly once with matching record and content");
    }

    // ── AC: Overwrite existing version → 409 ─────────────────────────────────

    [Fact]
    public async Task Publish_VersionAlreadyExists_ReturnsConflict()
    {
        // Arrange
        var existing = MakeRecord();
        var (handler, _) = BuildPublishHandler(existingRecord: existing);
        var cmd = ValidPublishCommand();

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(because: "duplicate version must be rejected");
        result.Error.Code.Should().Be("Conflict",
            because: "VERSION_EXISTS is a Conflict error");
        result.Error.Message.Should().Be("VERSION_EXISTS");
    }

    // ── AC: SchemaVersion "2" → 422 with diagnostic ───────────────────────────

    [Fact]
    public async Task Publish_UnsupportedSchemaVersion_ReturnsUnsupportedSchemaError()
    {
        // Arrange
        var (handler, _) = BuildPublishHandler(existingRecord: null);
        var cmd = ValidPublishCommand(schemaVersion: "2");

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(because: "schema version 2 is not supported");
        result.Error.Code.Should().Be("UNSUPPORTED_SCHEMA",
            because: "the error code must be UNSUPPORTED_SCHEMA");
        result.Error.Message.Should().Contain("2",
            because: "the diagnostic must include the offending schema version");
    }

    [Fact]
    public async Task Publish_SchemaVersionEmpty_ReturnsUnsupportedSchemaError()
    {
        // Arrange
        var (handler, _) = BuildPublishHandler(existingRecord: null);
        var cmd = ValidPublishCommand(schemaVersion: "");

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(because: "empty schema version is not 1 and must be rejected");
        result.Error.Code.Should().Be("UNSUPPORTED_SCHEMA");
    }

    // ── AC: Signature provided but empty → rejected ───────────────────────────

    [Fact]
    public async Task Publish_EmptySignatureProvided_ReturnsInvalidSignatureError()
    {
        // Arrange
        var (handler, _) = BuildPublishHandler(existingRecord: null);
        var cmd = ValidPublishCommand(signature: "");   // non-null but empty = invalid

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsFailure.Should().BeTrue(because: "an explicitly empty signature must be rejected");
        result.Error.Code.Should().Be("INVALID_SIGNATURE");
    }

    [Fact]
    public async Task Publish_NonEmptySignatureProvided_Succeeds()
    {
        // Arrange
        var (handler, _) = BuildPublishHandler(existingRecord: null);
        var cmd = ValidPublishCommand(signature: "valid-sig-abc123");

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "stub accepts any non-empty signature as valid");
    }

    [Fact]
    public async Task Publish_NullSignature_Succeeds()
    {
        // Arrange
        var (handler, _) = BuildPublishHandler(existingRecord: null);
        var cmd = ValidPublishCommand(signature: null);   // absent = no signature required

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue(
            because: "absent (null) signature is allowed");
    }

    // ── AC: SelectorContent round-trips opaque ────────────────────────────────

    [Fact]
    public async Task Publish_WithSelectorContent_StoredInRecord()
    {
        // Arrange
        const string selector = "{\"type\":\"sequential\",\"steps\":[\"a\",\"b\"]}";
        ArtifactRecord? capturedRecord = null;

        var repoMock = new Mock<IArtifactRepository>();
        repoMock
            .Setup(r => r.GetRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArtifactRecord?)null);
        repoMock
            .Setup(r => r.AddAsync(It.IsAny<ArtifactRecord>(), It.IsAny<ArtifactContent>(), It.IsAny<CancellationToken>()))
            .Callback<ArtifactRecord, ArtifactContent, CancellationToken>((r, _, _) => capturedRecord = r)
            .Returns(Task.CompletedTask);

        var handler = new PublishArtifactHandler(repoMock.Object, Clock());
        var cmd = ValidPublishCommand(selectorContent: selector);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        capturedRecord.Should().NotBeNull(because: "AddAsync must have been called");
        capturedRecord!.SelectorContent.Should().Be(selector,
            because: "SelectorContent must be stored verbatim (opaque passthrough)");
    }

    [Fact]
    public async Task Get_WithSelectorContent_ReturnedPayloadUnchanged()
    {
        // Arrange
        const string selector = "{\"type\":\"sequential\"}";
        var payload = new byte[] { 0xAB, 0xCD };
        var hash = ExpectedHash(payload);

        var record = new ArtifactRecord("art-sel", "1.0.0", "1", hash, FixedNow,
            signatureValid: true, selectorContent: selector);
        var content = new ArtifactContent("art-sel", "1.0.0", payload);

        var (handler, _) = BuildGetHandler(record, content);

        // Act
        var result = await handler.HandleAsync(new GetArtifactQuery("art-sel", "1.0.0"));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Payload.Should().BeEquivalentTo(payload,
            because: "the payload must round-trip unchanged");
    }

    // ── AC: DELETE with active LEASED run → 409 ───────────────────────────────

    [Fact]
    public async Task Delete_ArtifactInUseByActiveLease_ReturnsConflict()
    {
        // Arrange
        var record = MakeRecord();
        var (handler, _) = BuildDeleteHandler(record, isReferencedByActiveRun: true);

        // Act
        var result = await handler.HandleAsync(
            new DeleteArtifactCommand("artifact-001", "1.0.0"));

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "deletion must be blocked when a LEASED or RUNNING run holds a reference (invariant 7)");
        result.Error.Code.Should().Be("Conflict");
        result.Error.Message.Should().Be("ARTIFACT_IN_USE");
    }

    [Fact]
    public async Task Delete_NoActiveLeases_SoftDeletesRecord()
    {
        // Arrange
        var record = MakeRecord();
        var (handler, repoMock) = BuildDeleteHandler(record, isReferencedByActiveRun: false);

        // Act
        var result = await handler.HandleAsync(
            new DeleteArtifactCommand("artifact-001", "1.0.0"));

        // Assert
        result.IsSuccess.Should().BeTrue(because: "deletion must succeed when no active leases exist");
        record.IsDeleted.Should().BeTrue(because: "the record must be soft-deleted");
        repoMock.Verify(r => r.Update(record), Times.Once,
            "Update must be called so EF Core tracks the change");
    }

    [Fact]
    public async Task Delete_AlreadyDeletedRecord_ReturnsNotFound()
    {
        // Arrange
        var record = MakeRecord(isDeleted: true);
        var (handler, _) = BuildDeleteHandler(record);

        // Act
        var result = await handler.HandleAsync(
            new DeleteArtifactCommand("artifact-001", "1.0.0"));

        // Assert
        result.IsFailure.Should().BeTrue(because: "a previously deleted artifact is treated as not found");
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task Delete_NonExistentArtifact_ReturnsNotFound()
    {
        // Arrange
        var (handler, _) = BuildDeleteHandler(record: null);

        // Act
        var result = await handler.HandleAsync(
            new DeleteArtifactCommand("does-not-exist", "9.9.9"));

        // Assert
        result.IsFailure.Should().BeTrue(because: "deleting a non-existent artifact must return NotFound");
        result.Error.Code.Should().Be("NotFound");
    }

    // ── AC: GET returns ETag matching ContentHash ─────────────────────────────

    [Fact]
    public async Task Get_ExistingArtifact_ReturnsPayloadAndContentHash()
    {
        // Arrange
        var payload = new byte[] { 0x10, 0x20, 0x30 };
        var hash = ExpectedHash(payload);
        var record = new ArtifactRecord("art-a", "2.0.0", "1", hash, FixedNow,
            signatureValid: true, selectorContent: null);
        var content = new ArtifactContent("art-a", "2.0.0", payload);

        var (handler, _) = BuildGetHandler(record, content);

        // Act
        var result = await handler.HandleAsync(new GetArtifactQuery("art-a", "2.0.0"));

        // Assert
        result.IsSuccess.Should().BeTrue(because: "GET on a published artifact must succeed");
        result.Value.ContentHash.Should().Be(hash,
            because: "ContentHash in the result is the ETag and must match the stored hash");
        result.Value.Payload.Should().BeEquivalentTo(payload,
            because: "the raw payload bytes must be returned unchanged");
    }

    [Fact]
    public async Task Get_DeletedArtifact_ReturnsNotFound()
    {
        // Arrange
        var record = MakeRecord(isDeleted: true);
        var content = new ArtifactContent("artifact-001", "1.0.0", [0x01]);
        var (handler, _) = BuildGetHandler(record, content);

        // Act
        var result = await handler.HandleAsync(
            new GetArtifactQuery("artifact-001", "1.0.0"));

        // Assert
        result.IsFailure.Should().BeTrue(because: "soft-deleted artifacts must not be retrievable");
        result.Error.Code.Should().Be("NotFound");
    }

    [Fact]
    public async Task Get_NonExistentArtifact_ReturnsNotFound()
    {
        // Arrange
        var (handler, _) = BuildGetHandler(record: null, content: null);

        // Act
        var result = await handler.HandleAsync(
            new GetArtifactQuery("ghost", "0.0.1"));

        // Assert
        result.IsFailure.Should().BeTrue(because: "a missing artifact version returns NotFound");
        result.Error.Code.Should().Be("NotFound");
    }

    // ── AC: ContentHash is a deterministic SHA-256 hex digest ─────────────────

    [Fact]
    public async Task Publish_ContentHash_IsSha256HexOfPayload()
    {
        // Arrange
        var payload = "Hello OC-11"u8.ToArray();
        var expectedHash = ExpectedHash(payload);

        var repoMock = new Mock<IArtifactRepository>();
        repoMock
            .Setup(r => r.GetRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArtifactRecord?)null);
        repoMock
            .Setup(r => r.AddAsync(It.IsAny<ArtifactRecord>(), It.IsAny<ArtifactContent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new PublishArtifactHandler(repoMock.Object, Clock());
        var cmd = new PublishArtifactCommand("art-hash", "3.1.4", "1", payload, null, null);

        // Act
        var result = await handler.HandleAsync(cmd);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.ContentHash.Should().Be(expectedHash,
            because: "the contentHash must be the lower-case hex SHA-256 of the payload bytes");
        result.Value.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$",
            because: "a SHA-256 hex digest is always 64 lower-case hex characters");
    }

    // ── AC: StoredAt timestamp comes from IClock ──────────────────────────────

    [Fact]
    public async Task Publish_StoredAtTimestamp_MatchesFakeClock()
    {
        // Arrange
        var specificTime = new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);
        var fakeClock = new FakeClock(specificTime);

        ArtifactRecord? capturedRecord = null;
        var repoMock = new Mock<IArtifactRepository>();
        repoMock
            .Setup(r => r.GetRecordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ArtifactRecord?)null);
        repoMock
            .Setup(r => r.AddAsync(It.IsAny<ArtifactRecord>(), It.IsAny<ArtifactContent>(), It.IsAny<CancellationToken>()))
            .Callback<ArtifactRecord, ArtifactContent, CancellationToken>((r, _, _) => capturedRecord = r)
            .Returns(Task.CompletedTask);

        var handler = new PublishArtifactHandler(repoMock.Object, fakeClock);
        var cmd = ValidPublishCommand();

        // Act
        await handler.HandleAsync(cmd);

        // Assert
        capturedRecord.Should().NotBeNull();
        capturedRecord!.StoredAt.Should().Be(specificTime,
            because: "StoredAt must be taken from IClock.UtcNow — FakeClock must govern it");
    }
}
