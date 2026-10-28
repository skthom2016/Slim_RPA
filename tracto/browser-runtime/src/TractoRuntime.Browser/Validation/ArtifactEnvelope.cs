using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Validation;

/// <summary>
/// Signed artifact envelope wrapping a BrowserScript with metadata required
/// for pre-execution validation per CEC-001, CEC-002, and CEC-012.
/// </summary>
/// <remarks>
/// The Orchestrator issues an immutable artifact reference; the Runtime resolves
/// it to this envelope and must validate all fields before any browser action.
/// </remarks>
public sealed record ArtifactEnvelope
{
    /// <summary>
    /// Runtime type declared in the artifact — must equal "browser" for the Browser Runtime.
    /// A value of "desktop" or any other type is rejected with VALIDATION / browser.artifactTypeMismatch.
    /// </summary>
    public required string RuntimeType { get; init; }

    /// <summary>
    /// Script schema major version string.  Only "1" is accepted in v1; any other value is rejected.
    /// </summary>
    public required string SchemaMajor { get; init; }

    /// <summary>
    /// Cryptographic signature over the canonical artifact bytes (e.g. HMAC-SHA256).
    /// An empty or missing signature causes immediate VALIDATION rejection (unsigned artifact).
    /// </summary>
    public required string Signature { get; init; }

    /// <summary>
    /// Immutable digest of the artifact payload used to detect tampering.
    /// Must be present and non-empty; a missing digest is treated as unsigned.
    /// </summary>
    public required string ArtifactDigest { get; init; }

    /// <summary>The automation script to execute once validation passes.</summary>
    public required BrowserScript Script { get; init; }

    /// <summary>
    /// Policy references declared in the artifact and checked against the deployment policy store.
    /// </summary>
    public IReadOnlyList<string> PolicyRefs { get; init; } = [];

    /// <summary>
    /// When <see langword="true"/>, CSS selectors are explicitly approved by the deployment policy.
    /// When <see langword="false"/> (default), any step using a CSS selector is rejected with
    /// VALIDATION / browser.cssNotAllowed before any browser interaction.
    /// </summary>
    public bool AllowCss { get; init; } = false;
}
