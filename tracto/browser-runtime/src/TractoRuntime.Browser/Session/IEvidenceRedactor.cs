namespace TractoRuntime.Browser.Session;

/// <summary>
/// Redacts sensitive credential data from diagnostic evidence (BRE-006, BRW-US-014 AC#3, AC#4, AC#5).
///
/// <para>The following data must be redacted from all diagnostic artifacts (screenshots, trace
/// bodies, network summaries, and Orchestrator payloads):</para>
/// <list type="bullet">
///   <item>Authorization headers (Authorization, X-Auth-Token, Bearer tokens, etc.)</item>
///   <item>Set-Cookie and Cookie headers containing session tokens</item>
///   <item>Password form field values</item>
///   <item>Any <c>storageState</c> value or refresh token that may appear in trace bodies</item>
/// </list>
///
/// <para><b>CEC §6.2 / BRE-006 obligation:</b> No signed diagnostic artifact (screenshot, trace,
/// network summary, or Orchestrator payload) may contain a live session token, authorization header
/// value, password field value, or <c>storageState</c> JSON.</para>
/// </summary>
public interface IEvidenceRedactor
{
    /// <summary>
    /// Redacts credential values from a dictionary of HTTP headers.
    /// Returns a new dictionary with sensitive header values replaced by <c>[REDACTED]</c>.
    /// The input dictionary is not modified.
    /// </summary>
    Dictionary<string, string> RedactHeaders(IReadOnlyDictionary<string, string> headers);

    /// <summary>
    /// Redacts credential values from a JSON-serializable evidence dictionary.
    /// Returns a new dictionary with sensitive string values replaced by <c>[REDACTED]</c>.
    /// Nested objects and arrays are traversed recursively up to a bounded depth.
    /// The input dictionary is not modified.
    /// </summary>
    Dictionary<string, object?> RedactEvidence(IReadOnlyDictionary<string, object?> evidence);

    /// <summary>
    /// Redacts any <c>storageState</c>, token, or cookie-containing JSON string.
    /// Returns <c>[REDACTED:storageState]</c> when the input appears to be a Playwright
    /// <c>storageState</c> payload (contains "cookies" or "origins" at the JSON top level).
    /// Returns the input unchanged when it does not resemble session state.
    /// </summary>
    string RedactStorageState(string jsonCandidate);

    /// <summary>
    /// Determines whether a screenshot captured on the given page is safe to include in
    /// a signed diagnostic artifact.
    ///
    /// <para>When the page contains password fields or active session cookies and pixel-level
    /// masking cannot be guaranteed, returns <see langword="false"/> and sets
    /// <paramref name="safeReason"/> to a human-readable explanation.
    /// The caller must substitute a non-image artifact in this case (BRE-006 / AC#3).</para>
    /// </summary>
    /// <param name="pageContainsPasswordFields">
    /// <see langword="true"/> when the current page has visible <c>input[type=password]</c>
    /// elements (detected by Playwright locator query before screenshot capture).
    /// </param>
    /// <param name="pageContainsSessionCookies">
    /// <see langword="true"/> when the current browser context has active session cookies.
    /// </param>
    /// <param name="safeReason">
    /// When the method returns <see langword="false"/>, set to a non-null message explaining
    /// the substitution reason.  <see langword="null"/> when the screenshot is safe.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the screenshot is safe to include;
    /// <see langword="false"/> when it must be replaced with a non-image artifact.
    /// </returns>
    bool IsScreenshotSafe(
        bool pageContainsPasswordFields,
        bool pageContainsSessionCookies,
        out string? safeReason);
}
