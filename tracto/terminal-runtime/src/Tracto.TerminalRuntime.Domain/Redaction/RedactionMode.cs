namespace Tracto.TerminalRuntime.Domain.Redaction;

/// <summary>
/// Controls how captured command output is redacted before storage or forwarding.
/// </summary>
public enum RedactionMode
{
    /// <summary>
    /// Replaces every occurrence of any active secret token value with "***REDACTED***".
    /// This is the minimum permitted mode.
    /// </summary>
    MaskSecrets,

    /// <summary>
    /// Redacts the entire line whenever it contains a pattern that looks like a secret
    /// (length > 8, mixed-case + digit composition) in addition to replacing active tokens.
    /// </summary>
    MaskAll
}
