namespace Tracto.Orchestrator.Domain.Workers;

/// <summary>
/// Lifecycle state of a registered worker agent.
/// OFFLINE workers that held RUNNING runs trigger NeedsAttention on restart recovery.
/// </summary>
public enum WorkerState
{
    Available = 0,
    Leased    = 1,
    Running   = 2,
    Offline   = 3
}
