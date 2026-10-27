#nullable enable

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Inbound async callback message delivered by an external service after an async service.invoke step.</summary>
/// <param name="EventId">Globally unique identifier for this callback event; used for deduplication.</param>
/// <param name="SourceRunId">Run identifier of the execution that initiated the service.invoke step this callback answers.</param>
/// <param name="SourceStepId">Step identifier of the service.invoke step this callback answers.</param>
/// <param name="Nonce">Single-use token from the original callbackConfig.nonce; validators must verify this to prevent replay.</param>
/// <param name="Timestamp">ISO 8601 timestamp at which the external service produced this callback.</param>
/// <param name="ServiceOutcome">The outcome reported by the external service for the invoked operation.</param>
/// <param name="Signature">Optional cryptographic signature over the callback payload produced by the external service.</param>
/// <param name="Payload">Arbitrary structured data returned by the external service.</param>
/// <param name="Authenticated">Must not be accepted as a self-assertion of authentication status; validators must ignore or reject this field.</param>
/// <param name="ContinuationRuleId">Optional identifier of the static continuation rule to activate upon receipt of this callback.</param>
public record BusinessCallback(
    [property: JsonPropertyName("eventId")] string EventId,
    [property: JsonPropertyName("sourceRunId")] string SourceRunId,
    [property: JsonPropertyName("sourceStepId")] string SourceStepId,
    [property: JsonPropertyName("nonce")] string Nonce,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("serviceOutcome")] ServiceOutcome ServiceOutcome,
    [property: JsonPropertyName("signature")] string? Signature = null,
    [property: JsonPropertyName("payload")] JsonElement? Payload = null,
    [property: JsonPropertyName("authenticated")] bool? Authenticated = null,
    [property: JsonPropertyName("continuationRuleId")] string? ContinuationRuleId = null);
