using System.Text.RegularExpressions;
using Tracto.Orchestrator.Domain.Security;

namespace Tracto.Orchestrator.Application.Security;

/// <summary>
/// Scans string payloads for common secret patterns.
/// Used in CommitFinalResultCommand and IngestCallbackCommand to prevent secrets
/// being stored in the orchestrator database.
/// </summary>
public sealed class SecretScanService
{
    // ── Pattern catalogue ─────────────────────────────────────────────────────
    // Intentionally conservative: false positives are safer than false negatives.

    private static readonly (string Name, Regex Pattern)[] Patterns =
    [
        (
            "AWS_ACCESS_KEY",
            new Regex(@"(?<![A-Z0-9])AKIA[0-9A-Z]{16}(?![A-Z0-9])",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant)
        ),
        (
            "AWS_SECRET_KEY",
            new Regex(@"(?i)aws[_\-]?(secret[_\-]?access[_\-]?key|secret|access[_\-]?key)\s*[:=]\s*\S+",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant)
        ),
        (
            "PEM_PRIVATE_KEY",
            new Regex(@"-----BEGIN\s+(?:RSA |EC |OPENSSH |ENCRYPTED )?PRIVATE KEY-----",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant)
        ),
        (
            "PEM_BLOCK",
            new Regex(@"-----BEGIN\s+\w",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant)
        ),
        (
            "PEM_CERTIFICATE",
            new Regex(@"-----BEGIN\s+CERTIFICATE-----",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant)
        ),
        (
            "PASSWORD_ASSIGNMENT",
            new Regex(@"(?i)(password|passwd|pwd)[""'\s]*[:=][""'\s]*\S+",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant)
        ),
        (
            "SECRET_OR_API_KEY",
            new Regex(@"(?i)(secret|api[_\-]?key|apikey|token)[""'\s]*[:=][""'\s]*\S+",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant)
        ),
        (
            "BEARER_TOKEN",
            new Regex(@"(?i)(auth[_\-]?token|bearer)\s+[A-Za-z0-9\-._~+/]+=*",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant)
        ),
        (
            "PRIVATE_KEY_KEYWORD",
            new Regex(@"(?i)private[_\-]?key",
                      RegexOptions.Compiled | RegexOptions.CultureInvariant)
        ),
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="payload"/> against all secret patterns.
    /// Returns <see cref="SecretViolation"/> instances for every match found (empty when clean).
    /// Each violation includes a redacted snippet safe for logging.
    /// </summary>
    public IReadOnlyList<SecretViolation> Scan(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return Array.Empty<SecretViolation>();

        var violations = new List<SecretViolation>();

        foreach (var (name, pattern) in Patterns)
        {
            var match = pattern.Match(payload);
            if (match.Success)
            {
                var redacted = BuildRedactedSnippet(match.Value);
                violations.Add(SecretViolation.Create(name, fieldPath: string.Empty, redactedSnippet: redacted));
            }
        }

        return violations;
    }

    /// <summary>
    /// Scans all strings in <paramref name="payloads"/> and returns every violation found.
    /// </summary>
    public IReadOnlyList<SecretViolation> ScanAll(IEnumerable<string?> payloads)
    {
        var all = new List<SecretViolation>();
        foreach (var p in payloads)
            all.AddRange(Scan(p));
        return all;
    }

    /// <summary>
    /// Convenience method: returns <c>true</c> when the payload contains at least one
    /// secret pattern.
    /// </summary>
    public bool ContainsSecret(string? payload) =>
        !string.IsNullOrEmpty(payload) && Scan(payload).Count > 0;

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a sanitised snippet from a regex match value.
    /// The first four characters are kept to help operators confirm the detection;
    /// the remainder is replaced with asterisks so no raw secret is stored.
    /// </summary>
    private static string BuildRedactedSnippet(string matchValue)
    {
        const int visiblePrefixLength = 4;
        if (matchValue.Length <= visiblePrefixLength)
            return new string('*', matchValue.Length);

        return matchValue[..visiblePrefixLength] + new string('*', Math.Min(matchValue.Length - visiblePrefixLength, 12));
    }
}
