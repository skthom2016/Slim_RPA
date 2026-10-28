#nullable enable
namespace Tracto.Orchestrator.Domain.Artifacts;

/// <summary>
/// Stores the raw payload bytes for a published artifact version.
/// Separated from <see cref="ArtifactRecord"/> to allow lightweight metadata queries.
/// Domain layer — zero EF or ASP references.
/// </summary>
public sealed class ArtifactContent
{
    private ArtifactContent() { }

    public ArtifactContent(string artifactId, string version, byte[] payload)
    {
        ArtifactId = artifactId;
        Version = version;
        Payload = payload;
    }

    public string ArtifactId { get; private set; } = string.Empty;
    public string Version { get; private set; } = string.Empty;

    /// <summary>Raw artifact payload. Immutable once written.</summary>
    public byte[] Payload { get; private set; } = Array.Empty<byte>();
}
