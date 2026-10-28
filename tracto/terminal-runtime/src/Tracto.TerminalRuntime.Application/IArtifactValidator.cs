using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Application;

/// <summary>
/// Validates the cryptographic signature of an <see cref="ArtifactManifest"/>.
/// Infrastructure provides the concrete implementation; the Application layer
/// depends only on this abstraction.
/// </summary>
public interface IArtifactValidator
{
    /// <summary>
    /// Returns <c>true</c> when the manifest's <see cref="ArtifactManifest.Signature"/>
    /// is a valid HMAC-SHA256 of the canonical payload using the pre-shared key.
    /// </summary>
    bool IsSignatureValid(ArtifactManifest manifest);
}
