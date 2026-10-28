using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Tracto.Orchestrator.Domain.Security;
using IClock = Tracto.Orchestrator.Application.Abstractions.IClock;
// Explicit alias resolves CS0104 ambiguity with Application.Abstractions.ISecurityEventLog.
using ISecurityEventLog = Tracto.Orchestrator.Domain.Security.ISecurityEventLog;

namespace Tracto.Orchestrator.Api.Security;

/// <summary>
/// OC-17 — Validates JWT Bearer tokens on all /jobs/* and /workers/* routes.
/// Unauthenticated requests receive 401 and a SecurityEvent is appended.
///
/// The middleware does NOT wire ASP.NET Core authentication services: it performs
/// manual token validation so integration tests can swap the validation parameters
/// without touching the DI-registered authentication handler stack.
///
/// Token validation is intentionally lenient in test / development mode (no issuer or
/// audience validation) when <see cref="JwtAuthOptions.ValidateIssuerAndAudience"/> is false.
/// In production both should be true.
/// </summary>
public sealed class JwtAuthMiddleware
{
    private static readonly HashSet<string> ProtectedPrefixes =
        new(StringComparer.OrdinalIgnoreCase) { "/jobs", "/workers", "/admin" };

    private readonly RequestDelegate             _next;
    private readonly JwtAuthOptions             _options;
    private readonly ISecurityEventLog          _eventLog;
    private readonly IClock                     _clock;
    private readonly ILogger<JwtAuthMiddleware> _logger;

    public JwtAuthMiddleware(
        RequestDelegate              next,
        JwtAuthOptions               options,
        ISecurityEventLog            eventLog,
        IClock                       clock,
        ILogger<JwtAuthMiddleware>   logger)
    {
        _next     = next;
        _options  = options;
        _eventLog = eventLog;
        _clock    = clock;
        _logger   = logger;

        // MAJOR: warn at startup if no signing key is configured — prevents silent
        // authentication bypass when JwtSecret is accidentally omitted in production.
        if (_options.SigningKey is null)
        {
            _logger.LogWarning(
                "OC-17 JwtAuth: No signing key configured. JWT tokens are parsed but NOT " +
                "cryptographically verified. This is acceptable only in development/test environments. " +
                "Set Orchestrator:JwtSecret in production configuration.");
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresAuth(context.Request.Path))
        {
            var principal = TryExtractPrincipal(context, out var failure);

            if (principal is null)
            {
                var ip = context.Connection.RemoteIpAddress?.ToString();
                _logger.LogWarning(
                    "OC-17 JwtAuth: unauthenticated request to {Path} from {Ip}. {Failure}",
                    context.Request.Path, ip, failure);

                await _eventLog.AppendAsync(
                    new SecurityEvent(
                        id:         Guid.NewGuid(),
                        eventType:  "AUTH_FAILURE",
                        sourceIp:   ip,
                        occurredAt: _clock.UtcNow,
                        details:    $"Unauthenticated request to {context.Request.Path}: {failure}"),
                    context.RequestAborted);

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new { type  = "https://tracto.io/errors/Unauthorized",
                          title = "Unauthorized",
                          status = 401,
                          detail = "A valid JWT Bearer token is required." });
                return;
            }

            // Attach the validated principal so downstream handlers can inspect claims.
            context.User = principal;
        }

        await _next(context);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool RequiresAuth(PathString path)
    {
        foreach (var prefix in ProtectedPrefixes)
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private ClaimsPrincipal? TryExtractPrincipal(HttpContext context, out string failure)
    {
        failure = string.Empty;

        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            failure = "Missing or malformed Authorization header.";
            return null;
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            failure = "Empty token.";
            return null;
        }

        // In test / development mode with no signing key configured, skip crypto validation.
        if (_options.SigningKey is null)
        {
            // Parse the token without signature validation.
            var handler  = new JwtSecurityTokenHandler { MapInboundClaims = false };
            try
            {
                var jwt = handler.ReadJwtToken(token);
                var identity  = new ClaimsIdentity(jwt.Claims, "Bearer");
                return new ClaimsPrincipal(identity);
            }
            catch (Exception ex)
            {
                failure = $"Token parse failed: {ex.Message}";
                return null;
            }
        }

        // Production path: full signature + lifetime validation.
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = _options.SigningKey,
            ValidateLifetime         = true,
            ClockSkew                = TimeSpan.FromSeconds(30),
            ValidateIssuer           = _options.ValidateIssuerAndAudience,
            ValidateAudience         = _options.ValidateIssuerAndAudience,
            ValidIssuer              = _options.ValidIssuer,
            ValidAudience            = _options.ValidAudience,
            NameClaimType            = "sub",
        };

        var prodHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        try
        {
            var principal = prodHandler.ValidateToken(token, validationParams, out _);
            return principal;
        }
        catch (SecurityTokenException ex)
        {
            failure = ex.Message;
            return null;
        }
    }
}

/// <summary>JWT validation parameters injected via DI.</summary>
public sealed class JwtAuthOptions
{
    /// <summary>When null, the middleware parses but does NOT cryptographically verify the JWT (dev/test only).</summary>
    public SecurityKey? SigningKey { get; init; }

    /// <summary>When true, issuer and audience claims are validated against <see cref="ValidIssuer"/> and <see cref="ValidAudience"/>.</summary>
    public bool ValidateIssuerAndAudience { get; init; }
    public string? ValidIssuer   { get; init; }
    public string? ValidAudience { get; init; }
}
