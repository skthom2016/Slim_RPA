using System.Text.RegularExpressions;

namespace Tracto.Orchestrator.Api.Security;

/// <summary>
/// OC-17 — Logs all HTTP requests and responses at Information level.
/// Sanitisation rules:
///   • The Authorization header value is never logged (only "Bearer ***" is emitted).
///   • Any JSON field named "secretRefs" or "password" in request bodies is redacted.
/// This middleware does NOT buffer the response body to avoid allocation pressure
/// on large payloads; it logs status code and elapsed time instead.
/// </summary>
public sealed class AuditLoggingMiddleware
{
    // Redacts secretRefs array values and password values in JSON.
    private static readonly Regex SecretRefsRedactor =
        new(@"""(?i:secretRefs)""\s*:\s*\[([^\]]*)\]",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PasswordRedactor =
        new(@"""(?i:password)""\s*:\s*""[^""]*""",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly RequestDelegate              _next;
    private readonly ILogger<AuditLoggingMiddleware> _logger;

    public AuditLoggingMiddleware(
        RequestDelegate                   next,
        ILogger<AuditLoggingMiddleware>   logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var start  = DateTimeOffset.UtcNow;
        var sw     = System.Diagnostics.Stopwatch.StartNew();

        // Sanitise Authorization header before logging.
        var authRaw  = context.Request.Headers.Authorization.FirstOrDefault();
        var authSafe = SanitiseAuthHeader(authRaw);

        _logger.LogInformation(
            "AUDIT REQ  {Method} {Path}{Query} Authorization={Auth} TraceId={TraceId}",
            context.Request.Method,
            context.Request.Path,
            context.Request.QueryString,
            authSafe,
            context.TraceIdentifier);

        await _next(context);

        sw.Stop();

        _logger.LogInformation(
            "AUDIT RESP {Method} {Path} → {StatusCode} in {ElapsedMs}ms TraceId={TraceId}",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            sw.ElapsedMilliseconds,
            context.TraceIdentifier);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string SanitiseAuthHeader(string? raw)
    {
        if (string.IsNullOrEmpty(raw))               return "(none)";
        if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return "Bearer ***";
        if (raw.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return "Basic ***";
        return "***";
    }

    /// <summary>
    /// Redacts secretRefs values and password values in a JSON string.
    /// Called by application-layer code that logs request bodies; not applied to HTTP streams
    /// (streaming body buffering is deliberately avoided for performance).
    /// The output is always valid JSON: the redacted replacement is a well-formed string literal.
    /// </summary>
    public static string RedactBody(string? json)
    {
        if (string.IsNullOrEmpty(json)) return string.Empty;

        // Replace secretRefs array content with a single redacted placeholder.
        // e.g. "secretRefs":["a","b"] → "secretRefs":["***"]
        var redacted = SecretRefsRedactor.Replace(json, @"""secretRefs"":[""***""]");

        // Replace password field value with a redacted placeholder.
        // e.g. "password":"hunter2" → "password":"***"
        redacted = PasswordRedactor.Replace(redacted, @"""password"":""***""");

        return redacted;
    }
}
