using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace TractoRuntime.Browser.Session;

/// <summary>
/// Production implementation of <see cref="IEvidenceRedactor"/>.
///
/// Satisfies BRW-US-014 AC#3, AC#4, AC#5 and BRE-006:
/// <list type="bullet">
///   <item>Authorization headers, Bearer tokens, and session cookies are replaced with
///         <c>[REDACTED]</c> in all evidence dictionaries and header maps.</item>
///   <item>Evidence dictionaries are traversed recursively (up to
///         <see cref="MaxRecursionDepth"/> levels); no nested secret escapes redaction.</item>
///   <item>A Playwright <c>storageState</c> JSON payload (containing "cookies" or "origins"
///         keys) is classified as sensitive and fully replaced with a marker.</item>
///   <item>Screenshots on pages with password fields or active session cookies are flagged as
///         potentially unsafe; the caller must substitute a non-image artifact when pixel-level
///         masking cannot be guaranteed (AC#3, BRW-AT-27).</item>
/// </list>
/// </summary>
public sealed class EvidenceRedactor(ILogger<EvidenceRedactor> logger) : IEvidenceRedactor
{
    private const string RedactedMarker = "[REDACTED]";
    private const string StorageStateRedactedMarker = "[REDACTED:storageState]";
    private const int MaxRecursionDepth = 10;

    // HTTP header names that must always be redacted (case-insensitive)
    private static readonly HashSet<string> SensitiveHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Auth-Token",
        "X-Api-Key",
        "X-Session-Token",
        "Proxy-Authorization",
        "WWW-Authenticate"
    };

    // Evidence dictionary key fragments that indicate a sensitive value (lower-case)
    private static readonly string[] SensitiveKeyFragments =
    [
        "token",
        "secret",
        "password",
        "credential",
        "cookie",
        "authorization",
        "auth",
        "session",
        "storagestate",
        "apikey",
        "api_key",
        "access_key",
        "refresh_token",
        "refreshtoken"
    ];

    // -------------------------------------------------------------------------
    // IEvidenceRedactor
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Dictionary<string, string> RedactHeaders(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var result = new Dictionary<string, string>(headers.Count, StringComparer.OrdinalIgnoreCase);
        int redactedCount = 0;

        foreach (var (key, value) in headers)
        {
            if (SensitiveHeaderNames.Contains(key))
            {
                result[key] = RedactedMarker;
                redactedCount++;
            }
            else
            {
                result[key] = value;
            }
        }

        if (redactedCount > 0)
        {
            logger.LogDebug(
                "Redacted {Count} sensitive HTTP header(s) from evidence (BRE-006).", redactedCount);
        }

        return result;
    }

    /// <inheritdoc/>
    public Dictionary<string, object?> RedactEvidence(IReadOnlyDictionary<string, object?> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return RedactDictionary(evidence, depth: 0);
    }

    /// <inheritdoc/>
    public string RedactStorageState(string jsonCandidate)
    {
        ArgumentNullException.ThrowIfNull(jsonCandidate);

        if (LooksLikeStorageState(jsonCandidate))
        {
            logger.LogDebug(
                "storageState payload detected in evidence — replacing with redaction marker (BRE-006 / AC#4).");
            return StorageStateRedactedMarker;
        }

        return jsonCandidate;
    }

    /// <inheritdoc/>
    public bool IsScreenshotSafe(
        bool pageContainsPasswordFields,
        bool pageContainsSessionCookies,
        out string? safeReason)
    {
        if (pageContainsPasswordFields)
        {
            safeReason =
                "Page contains password fields; pixel-level masking cannot be guaranteed. " +
                "Screenshot replaced with non-image evidence artifact (BRE-006 / BRW-US-014 AC#3).";
            logger.LogWarning(
                "Screenshot flagged unsafe: page contains password fields — " +
                "substituting non-image evidence (BRE-006).");
            return false;
        }

        if (pageContainsSessionCookies)
        {
            safeReason =
                "Page renders session-authenticated content; screenshot may reveal session tokens " +
                "or authorization data. Screenshot replaced with non-image evidence artifact " +
                "(BRE-006 / BRW-US-014 AC#3).";
            logger.LogWarning(
                "Screenshot flagged unsafe: page has active session cookies — " +
                "substituting non-image evidence (BRE-006).");
            return false;
        }

        safeReason = null;
        return true;
    }

    // -------------------------------------------------------------------------
    // Recursive redaction helpers
    // -------------------------------------------------------------------------

    private Dictionary<string, object?> RedactDictionary(
        IReadOnlyDictionary<string, object?> dict,
        int depth)
    {
        if (depth > MaxRecursionDepth)
        {
            logger.LogWarning(
                "Evidence redaction reached max recursion depth ({MaxDepth}); " +
                "truncating nested content (BRE-006).", MaxRecursionDepth);
            return new Dictionary<string, object?>(1)
            {
                ["[truncated]"] = "[REDACTED:depth-exceeded]"
            };
        }

        var result = new Dictionary<string, object?>(dict.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in dict)
        {
            if (IsKeyNameSensitive(key))
            {
                result[key] = RedactedMarker;
            }
            else
            {
                result[key] = RedactValue(value, depth + 1);
            }
        }

        return result;
    }

    private object? RedactValue(object? value, int depth) => value switch
    {
        null => null,
        string s => RedactStringValue(s),
        // Dictionary<string,object?> is checked before IReadOnlyDictionary so the more-specific
        // concrete type matches first; both delegate to the same RedactDictionary helper.
        Dictionary<string, object?> d => RedactDictionary(d, depth),
        IReadOnlyDictionary<string, object?> d => RedactDictionary(d, depth),
        IEnumerable<object?> list => list.Select(v => RedactValue(v, depth)).ToList(),
        _ => value
    };

    private string RedactStringValue(string s)
    {
        // Fully redact Playwright storageState payloads found as string values (AC#4)
        if (LooksLikeStorageState(s))
        {
            logger.LogDebug(
                "Embedded storageState string found in evidence value — redacted (BRE-006).");
            return StorageStateRedactedMarker;
        }

        // Redact Bearer token values — they start with "Bearer " prefix (AC#3)
        if (s.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return RedactedMarker;

        // Redact Basic auth values — they start with "Basic " prefix
        if (s.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return RedactedMarker;

        return s;
    }

    // -------------------------------------------------------------------------
    // Static helpers
    // -------------------------------------------------------------------------

    private static bool IsKeyNameSensitive(string key)
    {
        var lower = key.ToLowerInvariant();
        return SensitiveKeyFragments.Any(frag =>
            lower.Contains(frag, StringComparison.Ordinal));
    }

    /// <summary>
    /// Heuristic: a string that starts with <c>{</c> and contains both "cookies" and/or
    /// "origins" at the JSON top level looks like a Playwright storageState payload.
    /// This catches the common case where a trace body or evidence value embeds the full JSON.
    /// </summary>
    private static bool LooksLikeStorageState(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)
            || !candidate.AsSpan().TrimStart().StartsWith("{".AsSpan()))
        {
            return false;
        }

        // Use a span-based scan before paying the JsonDocument.Parse cost
        if (!candidate.Contains("cookies", StringComparison.OrdinalIgnoreCase)
            && !candidate.Contains("origins", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(candidate);
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && (root.TryGetProperty("cookies", out _)
                       || root.TryGetProperty("origins", out _));
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
