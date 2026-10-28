#nullable enable

namespace Tracto.Contract.Validation;

/// <summary>
/// Immutable descriptor for a single approved service operation in the service registry.
/// </summary>
public record ServiceRegistryEntry(
    string ServiceRef,
    string OperationRef,
    string? BaseUrl,
    string HttpMethod,
    string? ResponseSchemaVersion,
    string? AuthPolicy,
    bool RequiresTls,
    IReadOnlyList<string>? AllowedRetryClasses);
