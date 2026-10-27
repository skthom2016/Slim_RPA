#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models;

[JsonConverter(typeof(JsonStringEnumConverter<TelemetryEventType>))]
public enum TelemetryEventType
{
    WorkerRegistered,
    JobLeased,
    JobAcquired,
    StepStarted,
    StepCompleted,
    RunCompleted,
    LeaseExpired,
    CancellationRequested,
    CancellationAcknowledged,
    HeartbeatSent,
    ReconcileStarted,
    ReconcileCompleted,
    EvidenceRecorded,
}
