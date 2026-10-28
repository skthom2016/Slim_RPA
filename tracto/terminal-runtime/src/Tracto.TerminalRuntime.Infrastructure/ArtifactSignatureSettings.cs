namespace Tracto.TerminalRuntime.Infrastructure;

/// <summary>
/// Bound from <c>appsettings.json</c> section <c>"ArtifactSignature"</c>.
/// The pre-shared key is kept out of logs, telemetry, and payloads
/// (CEC v1.0.1 invariant #4).
/// </summary>
public sealed class ArtifactSignatureSettings
{
    public const string SectionName = "ArtifactSignature";

    /// <summary>
    /// Base-64-encoded HMAC-SHA256 key used to verify artifact signatures.
    /// Must be set via environment variable or a secrets manager — never hard-coded.
    /// </summary>
    public string HmacKeyBase64 { get; init; } = string.Empty;
}
