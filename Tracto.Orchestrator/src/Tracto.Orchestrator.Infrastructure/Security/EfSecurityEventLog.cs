#nullable enable
using Microsoft.EntityFrameworkCore;
using Tracto.Orchestrator.Domain.Security;
using Tracto.Orchestrator.Infrastructure.Persistence;
using AppISecurityEventLog = Tracto.Orchestrator.Application.Abstractions.ISecurityEventLog;
using AppIClock = Tracto.Orchestrator.Application.Abstractions.IClock;

namespace Tracto.Orchestrator.Infrastructure.Security;

/// <summary>
/// EF Core implementation of the security event log (OC-07 / OC-17).
///
/// Implements two contracts:
///   • <see cref="ISecurityEventLog"/> (Domain): takes a fully-constructed <see cref="SecurityEvent"/>.
///   • <see cref="Application.Abstractions.ISecurityEventLog"/> (Application): takes raw string fields
///     and constructs the domain entity internally.
///
/// Each write uses its own short-lived <see cref="OrchestratorDbContext"/> opened from the
/// singleton <see cref="IDbContextFactory{T}"/>. This ensures security events are persisted
/// even when the originating request transaction is being rolled back.
/// </summary>
public sealed class EfSecurityEventLog : ISecurityEventLog, AppISecurityEventLog
{
    private readonly IDbContextFactory<OrchestratorDbContext> _factory;
    private readonly AppIClock                                _clock;

    public EfSecurityEventLog(IDbContextFactory<OrchestratorDbContext> factory, AppIClock clock)
    {
        _factory = factory;
        _clock   = clock;
    }

    // ── Domain interface: ISecurityEventLog.AppendAsync(SecurityEvent) ────────

    /// <inheritdoc/>
    public async Task AppendAsync(
        SecurityEvent     securityEvent,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.SecurityEvents.Add(securityEvent);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ── Application interface: ISecurityEventLog.AppendAsync(string …) ────────

    /// <inheritdoc/>
    async Task AppISecurityEventLog.AppendAsync(
        string            eventType,
        string?           sourceIp,
        string            details,
        CancellationToken cancellationToken)
    {
        var evt = new SecurityEvent(
            Guid.NewGuid(),
            eventType,
            sourceIp,
            _clock.UtcNow,
            details);

        await AppendAsync(evt, cancellationToken);
    }
}
