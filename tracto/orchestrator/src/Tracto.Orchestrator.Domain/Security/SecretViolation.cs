#nullable enable
namespace Tracto.Orchestrator.Domain.Security;

/// <summary>
/// Value object produced by <c>SecretScanService</c> for each detected secret pattern.
/// Immutable; carries only the structural description — never the raw matched value.
/// </summary>
/// <param name="PatternName">
///   Human-readable label for the pattern that fired
///   (e.g. "AWS_ACCESS_KEY", "PEM_PRIVATE_KEY", "PASSWORD_ASSIGNMENT").
/// </param>
/// <param name="FieldPath">
///   JSON pointer or property name within the scanned payload where the violation was found.
///   Empty string when the location cannot be determined (e.g. flat string scan).
/// </param>
/// <param name="RedactedSnippet">
///   A short snippet of the matching text with the secret portion replaced by asterisks,
///   so operators can confirm the detection without re-exposing the secret.
///   Example: "AKIA************XYZW"
/// </param>
public sealed record SecretViolation(
    string PatternName,
    string FieldPath,
    string RedactedSnippet)
{
    /// <summary>
    /// Factory that validates the mandatory fields and returns a violation.
    /// </summary>
    /// <param name="patternName">Required; must not be blank.</param>
    /// <param name="fieldPath">Optional JSON pointer or property name; defaults to empty string when not applicable.</param>
    /// <param name="redactedSnippet">Required; must not be blank. Must NOT contain the raw secret value.</param>
    public static SecretViolation Create(string patternName, string fieldPath, string redactedSnippet)
    {
        if (string.IsNullOrWhiteSpace(patternName))
            throw new ArgumentException("PatternName is required.", nameof(patternName));
        if (string.IsNullOrWhiteSpace(redactedSnippet))
            throw new ArgumentException("RedactedSnippet is required and must not be blank.", nameof(redactedSnippet));

        return new SecretViolation(
            patternName,
            fieldPath ?? string.Empty,
            redactedSnippet);
    }

    /// <summary>
    /// Produces a sanitised, single-line string safe for logging (no raw secret).
    /// </summary>
    public override string ToString() =>
        $"[{PatternName}] at '{FieldPath}': {RedactedSnippet}";
}
