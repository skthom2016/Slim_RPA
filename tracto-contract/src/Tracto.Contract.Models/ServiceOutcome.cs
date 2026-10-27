#nullable enable

using System.Text.Json.Serialization;

namespace Tracto.Contract.Models;

[JsonConverter(typeof(JsonStringEnumConverter<ServiceOutcome>))]
public enum ServiceOutcome
{
    [JsonPropertyName("SUCCESS")]
    Success,

    [JsonPropertyName("BUSINESS_REJECTION")]
    BusinessRejection,

    [JsonPropertyName("TECHNICAL_FAILURE")]
    TechnicalFailure,

    [JsonPropertyName("TRANSIENT_FAILURE")]
    TransientFailure,

    [JsonPropertyName("UNKNOWN_OUTCOME")]
    UnknownOutcome,
}
