#nullable enable
namespace Tracto.Orchestrator.Domain.Artifacts;

/// <summary>
/// Aggregate root representing a published artifact version.
/// Invariant: versions are immutable once published; deletion blocked while any lease references them.
/// Domain layer — zero EF or ASP references.
/// </summary>
public sealed class ArtifactRecord
{
    private ArtifactRecord() { }

    public ArtifactRecord(
        string artifactId,
        string version,
        string schemaVersion,
        string contentHash,
        DateTimeOffset storedAt,
        bool signatureValid,
        string? selectorContent,
        string runtimeType = "",
        string[]? requiredCapabilities = null)
    {
        ArtifactId = artifactId;
        Version = version;
        SchemaVersion = schemaVersion;
        ContentHash = contentHash;
        StoredAt = storedAt;
        IsDeleted = false;
        SignatureValid = signatureValid;
        SelectorContent = selectorContent;
        RuntimeType = runtimeType;
        RequiredCapabilities = requiredCapabilities ?? [];
    }

    public string ArtifactId { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;
    public string SchemaVersion { get; private set; } = string.Empty;
    public string ContentHash { get; private set; } = string.Empty;
    public DateTimeOffset StoredAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public bool SignatureValid { get; private set; }

    /// <summary>Stored opaque; not interpreted by the orchestrator.</summary>
    public string? SelectorContent { get; private set; }

    /// <summary>
    /// Runtime type required to execute this artifact (e.g. "dotnet", "python", "browser").
    /// Used by PullJobCommandHandler to match queued runs to workers.
    /// </summary>
    public string RuntimeType { get; private set; } = string.Empty;

    /// <summary>
    /// Capability names that a worker must advertise in order to be eligible
    /// for runs based on this artifact.  All entries must be present in
    /// <c>WorkerCapability.ActionNames</c>.  Empty means no specific capabilities required.
    /// </summary>
    public string[] RequiredCapabilities { get; private set; } = [];

    /// <summary>
    /// Soft-deletes this artifact version.
    /// The caller must verify no active leases exist before calling this.
    /// </summary>
    public void SoftDelete() => IsDeleted = true;
}
