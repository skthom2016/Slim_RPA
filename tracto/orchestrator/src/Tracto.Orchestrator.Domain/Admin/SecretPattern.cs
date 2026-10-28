#nullable enable
namespace Tracto.Orchestrator.Domain.Admin;

using System.Text.RegularExpressions;

/// <summary>
/// Domain value-object and static helper that identifies and redacts secret-like strings
/// from free-text fields before they are included in telemetry bundles or API responses.
///
/// Rules (OC-13):
///   - Any field value that matches a known secret pattern must be replaced with [REDACTED].
///   - Patterns are intentionally broad: prefer a false positive (over-redact) over a leak.
///   - The domain never stores, logs, or returns plaintext secrets (invariant 4).
/// </summary>
public static class SecretPattern
{
    // ── Compiled pattern list ─────────────────────────────────────────────────

    /// <summary>
    /// Patterns that identify secret-bearing field values.
    /// A single match anywhere in the string causes the entire value to be replaced.
    /// </summary>
    private static readonly Regex[] _patterns =
    [
        // Base64-encoded 32+ byte blobs commonly used as HMAC / JWT signing keys.
        new Regex(@"^[A-Za-z0-9+/]{43,}={0,2}$",
                  RegexOptions.Compiled | RegexOptions.Singleline),

        // Hex strings of 32+ chars (raw SHA-256 or AES keys).
        new Regex(@"\b[0-9a-fA-F]{64,}\b",
                  RegexOptions.Compiled | RegexOptions.Singleline),

        // "password", "secret", "apikey", "api_key", "token", "credential" followed
        // by '=' or ':' and a non-whitespace value (in query strings or JSON snippets).
        new Regex(@"(?i)(password|secret|api[_-]?key|token|credential|private[_-]?key)\s*[=:]\s*\S+",
                  RegexOptions.Compiled),

        // PEM-style private key headers.
        new Regex(@"-----BEGIN\s+\w+\s+PRIVATE KEY-----",
                  RegexOptions.Compiled | RegexOptions.IgnoreCase),

        // Bearer / Basic auth header values.
        new Regex(@"\b(Bearer|Basic)\s+[A-Za-z0-9+/\-_=.]{8,}",
                  RegexOptions.Compiled),

        // Connection strings that contain a password segment.
        new Regex(@"[Pp]assword\s*=\s*[^;""'\s]+",
                  RegexOptions.Compiled),

        // AWS-style access key IDs.
        new Regex(@"\b(AKIA|ASIA|AROA)[A-Z0-9]{16}\b",
                  RegexOptions.Compiled),
    ];

    // ── Redaction constant ─────────────────────────────────────────────────────

    /// <summary>Replacement text inserted for redacted values.</summary>
    public const string RedactedPlaceholder = "[REDACTED]";

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="value"/> contains at least one
    /// recognized secret pattern.
    /// </summary>
    public static bool IsSecret(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var regex in _patterns)
            if (regex.IsMatch(value)) return true;
        return false;
    }

    /// <summary>
    /// Returns the original <paramref name="value"/> unchanged when it does not
    /// match any secret pattern, or <see cref="RedactedPlaceholder"/> when it does.
    ///
    /// The entire string is replaced rather than the matched sub-string to avoid
    /// partial leakage of high-entropy prefixes/suffixes.
    /// </summary>
    public static string Redact(string? value)
    {
        if (IsSecret(value)) return RedactedPlaceholder;
        return value ?? string.Empty;
    }

    /// <summary>
    /// Redacts all secret patterns within a block of free-form text by replacing
    /// matched sub-strings inline. Use this for multi-field JSON payloads or log lines
    /// where a full-value replacement would discard surrounding context.
    /// </summary>
    public static string RedactInline(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string result = text;
        foreach (var regex in _patterns)
            result = regex.Replace(result, RedactedPlaceholder);
        return result;
    }
}
