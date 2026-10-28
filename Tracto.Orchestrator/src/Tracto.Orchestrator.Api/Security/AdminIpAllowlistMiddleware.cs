using System.Net;
using Tracto.Orchestrator.Domain.Security;
using Tracto.Orchestrator.Domain.Settings;
using IClock = Tracto.Orchestrator.Application.Abstractions.IClock;
// Explicit alias resolves CS0104 ambiguity with Application.Abstractions.ISecurityEventLog.
using ISecurityEventLog = Tracto.Orchestrator.Domain.Security.ISecurityEventLog;

namespace Tracto.Orchestrator.Api.Security;

/// <summary>
/// OC-17 — For /admin/* routes in Connected mode, rejects requests from IP addresses
/// that fall outside the configured <see cref="OrchestratorSettings.AllowedAdminCidr"/> block.
/// Blocked requests receive 403 and a SecurityEvent is appended.
/// In Isolated mode or when AllowedAdminCidr is null, the middleware is a pass-through.
///
/// CIDR membership is evaluated by the Domain value object <see cref="CidrRange"/>
/// rather than a bespoke implementation, so all parsing and containment logic is shared.
/// </summary>
public sealed class AdminIpAllowlistMiddleware
{
    private readonly RequestDelegate                     _next;
    private readonly OrchestratorSettings                _settings;
    private readonly ISecurityEventLog                   _eventLog;
    private readonly IClock                              _clock;
    private readonly ILogger<AdminIpAllowlistMiddleware> _logger;

    // Parsed from AllowedAdminCidr at startup using the Domain CidrRange value object.
    private readonly CidrRange? _allowedCidr;
    private readonly bool       _isActive;

    public AdminIpAllowlistMiddleware(
        RequestDelegate                      next,
        OrchestratorSettings                 settings,
        ISecurityEventLog                    eventLog,
        IClock                               clock,
        ILogger<AdminIpAllowlistMiddleware>  logger)
    {
        _next     = next;
        _settings = settings;
        _eventLog = eventLog;
        _clock    = clock;
        _logger   = logger;

        // The middleware is only active in Connected mode when a CIDR is specified.
        if (settings.Mode == DeploymentMode.Connected && !string.IsNullOrWhiteSpace(settings.AllowedAdminCidr))
        {
            var parseResult = CidrRange.TryParse(settings.AllowedAdminCidr);
            if (parseResult.IsSuccess)
            {
                _allowedCidr = parseResult.Value;
                _isActive    = true;
            }
            else
            {
                _logger.LogWarning(
                    "OC-17 AdminIpAllowlist: Failed to parse AllowedAdminCidr '{Cidr}': {Error}. " +
                    "Admin IP allowlist will NOT be enforced.",
                    settings.AllowedAdminCidr, parseResult.Error.Message);
            }
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_isActive && context.Request.Path.StartsWithSegments("/admin", StringComparison.OrdinalIgnoreCase))
        {
            var remoteIp = context.Connection.RemoteIpAddress;

            // CidrRange.Contains() handles IPv4-mapped-IPv6 unwrapping internally.
            if (remoteIp is null || !_allowedCidr!.Contains(remoteIp))
            {
                var ipStr = remoteIp?.ToString() ?? "unknown";
                _logger.LogWarning(
                    "OC-17 AdminIpAllowlist: blocked {Ip} on {Path} (allowed CIDR: {Cidr})",
                    ipStr, context.Request.Path, _settings.AllowedAdminCidr);

                await _eventLog.AppendAsync(
                    new SecurityEvent(
                        id:         Guid.NewGuid(),
                        eventType:  "ADMIN_IP_BLOCKED",
                        sourceIp:   ipStr,
                        occurredAt: _clock.UtcNow,
                        details:    $"IP {ipStr} not in allowlist ({_settings.AllowedAdminCidr}) for {context.Request.Path}"),
                    context.RequestAborted);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new { type   = "https://tracto.io/errors/Forbidden",
                          title  = "Forbidden",
                          status = 403,
                          detail = "Your IP address is not permitted to access admin routes." });
                return;
            }
        }

        await _next(context);
    }
}
