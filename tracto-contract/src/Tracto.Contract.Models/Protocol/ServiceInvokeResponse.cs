#nullable enable

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Response returned from a service operation invocation.</summary>
/// <param name="ServiceOutcome">The outcome reported by the service for the invoked operation.</param>
/// <param name="ActionCode">Service-specific action or rejection code, if provided.</param>
/// <param name="Payload">Structured data returned by the service; schema is service-specific.</param>
/// <param name="ErrorDetail">Stable detail code describing a failure, if applicable.</param>
public record ServiceInvokeResponse(
    [property: JsonPropertyName("serviceOutcome")] ServiceOutcome ServiceOutcome,
    [property: JsonPropertyName("actionCode")] string? ActionCode = null,
    [property: JsonPropertyName("payload")] JsonElement? Payload = null,
    [property: JsonPropertyName("errorDetail")] string? ErrorDetail = null);
