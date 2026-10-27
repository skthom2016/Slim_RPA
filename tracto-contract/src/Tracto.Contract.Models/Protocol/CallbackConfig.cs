#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models.Protocol;

/// <summary>Configuration for async callback delivery on a service.invoke step.</summary>
/// <param name="TimeoutMs">Maximum milliseconds to wait for the async callback before timing out.</param>
/// <param name="Nonce">Single-use token embedded in the callback request to prevent replay.</param>
public record CallbackConfig(
    [property: JsonPropertyName("timeoutMs")] int TimeoutMs,
    [property: JsonPropertyName("nonce")] string Nonce);
