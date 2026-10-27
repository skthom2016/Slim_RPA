#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ErrorCategory>))]
public enum ErrorCategory
{
    [JsonPropertyName("VALIDATION")]
    Validation,

    [JsonPropertyName("TIMEOUT")]
    Timeout,

    [JsonPropertyName("POLICY_DENIED")]
    PolicyDenied,

    [JsonPropertyName("TRUST_FAILURE")]
    TrustFailure,

    [JsonPropertyName("TRANSPORT")]
    Transport,

    [JsonPropertyName("EXECUTION")]
    Execution,

    [JsonPropertyName("CONCURRENCY")]
    Concurrency,

    [JsonPropertyName("RESOURCE")]
    Resource,

    [JsonPropertyName("SECURITY")]
    Security,

    [JsonPropertyName("CANCELLED")]
    Cancelled,

    [JsonPropertyName("UNKNOWN")]
    Unknown,
}
