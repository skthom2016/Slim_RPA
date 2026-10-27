#nullable enable

using System.Text.Json.Serialization;
using Tracto.Contract.Models.Envelope;
using Tracto.Contract.Models.Execution;
using Tracto.Contract.Models.Telemetry;

namespace Tracto.Contract.Models;

// Top-level record types
[JsonSerializable(typeof(AutomationEnvelope))]
[JsonSerializable(typeof(InputDeclaration))]
[JsonSerializable(typeof(OutputDeclaration))]
[JsonSerializable(typeof(VarDeclaration))]
[JsonSerializable(typeof(StepDefinition))]
[JsonSerializable(typeof(TargetMap))]
[JsonSerializable(typeof(TargetDefinition))]
[JsonSerializable(typeof(TypedRef))]
[JsonSerializable(typeof(OnFailureConfig))]
[JsonSerializable(typeof(RunResult))]
[JsonSerializable(typeof(StepResult))]
[JsonSerializable(typeof(AttemptCheckpoint))]
[JsonSerializable(typeof(EvidenceRef))]
[JsonSerializable(typeof(StateTransition))]
[JsonSerializable(typeof(TransitionResult))]
[JsonSerializable(typeof(TelemetryEvent))]
[JsonSerializable(typeof(EvidenceManifest))]
[JsonSerializable(typeof(EvidenceEntry))]
// Top-level enum types
[JsonSerializable(typeof(RunState))]
[JsonSerializable(typeof(StepState))]
[JsonSerializable(typeof(ErrorCategory))]
[JsonSerializable(typeof(ServiceOutcome))]
[JsonSerializable(typeof(RuntimeType))]
[JsonSerializable(typeof(RetryClass))]
[JsonSerializable(typeof(HostClass))]
[JsonSerializable(typeof(WorkerHealthStatus))]
[JsonSerializable(typeof(EvidenceClassification))]
[JsonSerializable(typeof(ReconcileDisposition))]
[JsonSerializable(typeof(TelemetryEventType))]
internal partial class TractoContractModelsSerializerContext : JsonSerializerContext { }
