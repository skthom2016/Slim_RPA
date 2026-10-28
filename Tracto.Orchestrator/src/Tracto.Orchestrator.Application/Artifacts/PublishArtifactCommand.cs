#nullable enable
using System.Security.Cryptography;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Artifacts;
using Tracto.Orchestrator.Domain.Common;

namespace Tracto.Orchestrator.Application.Artifacts;

/// <summary>
/// Command: publish (store) a new artifact version.
/// Idempotency: same ArtifactId+Version already exists → 409 VERSION_EXISTS.
/// Schema gate: only SchemaVersion "1" accepted → 422 UNSUPPORTED_SCHEMA:{version}.
/// Signature stub: any non-empty string accepted as valid; empty signature treated as absent.
/// </summary>
public sealed record PublishArtifactCommand(
    string ArtifactId,
    string Version,
    string SchemaVersion,
    byte[] Payload,
    string? Signature,
    string? SelectorContent);

public sealed record PublishArtifactResult(
    string ArtifactId,
    string Version,
    string ContentHash);

public sealed class PublishArtifactHandler
{
    private readonly IArtifactRepository _artifacts;
    private readonly IClock _clock;

    public PublishArtifactHandler(IArtifactRepository artifacts, IClock clock)
    {
        _artifacts = artifacts;
        _clock = clock;
    }

    public async Task<Result<PublishArtifactResult, Error>> HandleAsync(
        PublishArtifactCommand cmd,
        CancellationToken cancellationToken = default)
    {
        // Schema gate
        if (cmd.SchemaVersion != "1")
            return Result<PublishArtifactResult, Error>.Fail(
                Error.Create("UNSUPPORTED_SCHEMA", $"UNSUPPORTED_SCHEMA:{cmd.SchemaVersion}"));

        // Signature stub: an explicitly empty (non-null, zero-length) string is rejected.
        // Any non-empty string is accepted as valid. Null means no signature provided.
        if (cmd.Signature is not null && cmd.Signature.Length == 0)
            return Result<PublishArtifactResult, Error>.Fail(
                Error.Create("INVALID_SIGNATURE", "Signature provided but invalid."));

        // signatureValid: true only when a non-empty signature was supplied; false when absent.
        bool signatureValid = cmd.Signature is { Length: > 0 };

        // Overwrite guard — a soft-deleted version is treated as non-existing and may be re-published.
        var existing = await _artifacts.GetRecordAsync(cmd.ArtifactId, cmd.Version, cancellationToken);
        if (existing is not null && !existing.IsDeleted)
            return Result<PublishArtifactResult, Error>.Fail(
                Error.Conflict("VERSION_EXISTS"));

        // Compute content hash
        var hash = Convert.ToHexString(SHA256.HashData(cmd.Payload)).ToLowerInvariant();

        var record = new ArtifactRecord(
            artifactId: cmd.ArtifactId,
            version: cmd.Version,
            schemaVersion: cmd.SchemaVersion,
            contentHash: hash,
            storedAt: _clock.UtcNow,
            signatureValid: signatureValid,
            selectorContent: cmd.SelectorContent);

        var content = new ArtifactContent(cmd.ArtifactId, cmd.Version, cmd.Payload);

        await _artifacts.AddAsync(record, content, cancellationToken);

        return Result<PublishArtifactResult, Error>.Ok(
            new PublishArtifactResult(cmd.ArtifactId, cmd.Version, hash));
    }
}
