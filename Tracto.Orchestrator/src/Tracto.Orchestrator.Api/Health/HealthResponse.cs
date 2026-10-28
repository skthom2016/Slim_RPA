namespace Tracto.Orchestrator.Api.Health;

/// <summary>
/// Response body for <c>GET /health</c>.
/// </summary>
/// <param name="Status">Always "healthy" when the endpoint is reachable.</param>
/// <param name="DbConnected">True when the database can be reached and EnsureCreated succeeded.</param>
/// <param name="QueueDepth">Number of QUEUED runs waiting for a worker (0 until run storage is implemented in OC-01).</param>
/// <param name="Mode">The deployment mode this instance is running in (Connected or Isolated).</param>
public sealed record HealthResponse(
    string Status,
    bool DbConnected,
    int QueueDepth,
    string Mode
);
