namespace Tracto.TerminalRuntime.Infrastructure.Commands;

/// <summary>
/// Configuration for the command allowlist registry (TER-US-006).
/// Bound from the <c>CommandRegistry</c> section of appsettings.json.
/// </summary>
public sealed class CommandRegistrySettings
{
    public const string SectionName = "CommandRegistry";

    /// <summary>Absolute path to the signed deployment policy JSON file.</summary>
    public string PolicyFilePath { get; init; } = string.Empty;

    /// <summary>
    /// Base-64-encoded HMAC-SHA256 key used to verify the policy document signature.
    /// Never written to logs, telemetry, evidence records, or Orchestrator payloads
    /// (CEC v1.0.1 invariant #4).
    /// </summary>
    public string HmacKeyBase64 { get; init; } = string.Empty;

    /// <summary>
    /// When <c>true</c>, the registry is allowed to load an unsigned policy file
    /// (i.e. when <see cref="HmacKeyBase64"/> is not configured).
    /// Default is <c>false</c> (fail-closed). Set to <c>true</c> only in development
    /// environments where no signing key has been provisioned.
    /// </summary>
    public bool AllowUnsignedPolicy { get; init; } = false;

    /// <summary>
    /// Approved root directories from which executable paths declared in the policy
    /// file are permitted to be read. Any executable path that does not resolve under
    /// one of these roots is rejected by <see cref="Sha256Verifier"/> before file I/O.
    /// An empty array (the default) denies all executable paths — approved roots must
    /// be explicitly configured in production.
    /// </summary>
    public string[] ApprovedExecutableRoots { get; init; } = [];
}
