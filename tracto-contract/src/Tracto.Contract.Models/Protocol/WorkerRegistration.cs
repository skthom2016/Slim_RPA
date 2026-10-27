#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Capability advertisement message sent by a worker at registration.</summary>
/// <param name="WorkerId">Stable unique identity of the worker.</param>
/// <param name="RuntimeType">Runtime environment the worker operates in.</param>
/// <param name="WorkerVersion">Semantic version of the worker software.</param>
/// <param name="ProtocolVersion">Wire protocol version in major.minor form.</param>
/// <param name="SupportedSchemaMajors">List of contract schema major versions this worker supports.</param>
/// <param name="CapabilityDigest">SHA-256 integrity hint computed over key worker fields and capabilities.</param>
/// <param name="HostClass">Whether the worker operates in an interactive desktop session or a headless environment.</param>
/// <param name="InteractiveSessionId">Session identifier for the interactive desktop session; null when headless.</param>
/// <param name="AdvertisedCapabilities">Full capability advertisement for this worker.</param>
/// <param name="Health">Current health status of the worker.</param>
public record WorkerRegistration(
    [property: JsonPropertyName("workerId")] string WorkerId,
    [property: JsonPropertyName("runtimeType")] RuntimeType RuntimeType,
    [property: JsonPropertyName("workerVersion")] string WorkerVersion,
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("supportedSchemaMajors")] IReadOnlyList<int> SupportedSchemaMajors,
    [property: JsonPropertyName("capabilityDigest")] string CapabilityDigest,
    [property: JsonPropertyName("hostClass")] HostClass HostClass,
    [property: JsonPropertyName("interactiveSessionId")] string? InteractiveSessionId,
    [property: JsonPropertyName("advertisedCapabilities")] AdvertisedCapabilities AdvertisedCapabilities,
    [property: JsonPropertyName("health")] WorkerHealth Health);
