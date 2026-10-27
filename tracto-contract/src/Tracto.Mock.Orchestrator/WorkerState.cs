#nullable enable

using Tracto.Contract.Models.Protocol;

namespace Tracto.Mock.Orchestrator;

/// <summary>Tracks a registered worker's current state within the mock orchestrator.</summary>
internal sealed class WorkerState
{
    public WorkerRegistration Registration { get; init; } = default!;
    public string? ActiveLeaseId { get; set; }
    public bool    IsBusy        => ActiveLeaseId is not null;
}
