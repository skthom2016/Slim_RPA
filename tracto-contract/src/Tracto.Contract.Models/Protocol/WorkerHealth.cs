#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Current health status reported by the worker at registration time.</summary>
/// <param name="Status">Coarse health status of the worker.</param>
/// <param name="Detail">Optional human-readable description of the health status.</param>
public record WorkerHealth(
    [property: JsonPropertyName("status")] WorkerHealthStatus Status,
    [property: JsonPropertyName("detail")] string? Detail = null);
