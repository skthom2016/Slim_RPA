using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Application.Heartbeat;

/// <summary>
/// Sends a single outbound heartbeat POST to the Orchestrator control plane on behalf of
/// the Terminal worker (TER-US-018, AC-1).
///
/// CEC v1.0.1 invariant #4: no plaintext secrets may appear in any
/// <see cref="HeartbeatRecord"/> field.
/// CEC v1.0.1 invariant #5: only business/gateway calls are made — no direct third-party
/// API, database, or raw-HTTP calls from Terminal command handlers.
/// </summary>
public interface IHeartbeatSender
{
    /// <summary>
    /// Delivers one heartbeat to the Orchestrator.
    ///
    /// Returns <c>Ok(true)</c> on HTTP 2xx.
    /// Returns <c>Fail</c> when the endpoint is temporarily unreachable, returns a
    /// non-success status, or the request times out.  The caller decides whether to
    /// increment a failure counter or escalate — this method never throws for transport errors.
    /// </summary>
    Task<Result<bool, Error>> SendAsync(HeartbeatRecord record, CancellationToken ct = default);
}
