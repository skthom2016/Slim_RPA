using Tracto.TerminalRuntime.Application.Cleanup;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Cleanup;

namespace Tracto.TerminalRuntime.Infrastructure.Cleanup;

/// <summary>
/// In-memory implementation of <see cref="IStaticContinuationMappingStore"/>.
///
/// Mappings are loaded once at application start-up from configuration and are
/// immutable at runtime, ensuring that no dynamic scriptRef from a callback
/// payload can be substituted for the configured value (CEC v1.0.1 AC-4).
/// </summary>
public sealed class InMemoryStaticContinuationMappingStore : IStaticContinuationMappingStore
{
    private readonly IReadOnlyDictionary<string, StaticContinuationMapping> _mappings;

    public InMemoryStaticContinuationMappingStore(
        IEnumerable<StaticContinuationMapping> mappings)
    {
        _mappings = mappings
            .ToDictionary(m => m.MappingRef, StringComparer.Ordinal);
    }

    public Task<Result<StaticContinuationMapping, Error>> GetMappingAsync(
        string mappingRef,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mappingRef))
            return Task.FromResult(
                Result<StaticContinuationMapping, Error>.Fail(
                    Error.Validation(
                        "ContinuationMapping.RefRequired",
                        "mappingRef must not be null or whitespace.")));

        if (_mappings.TryGetValue(mappingRef, out var mapping))
            return Task.FromResult(
                Result<StaticContinuationMapping, Error>.Ok(mapping));

        return Task.FromResult(
            Result<StaticContinuationMapping, Error>.Fail(
                Error.General(
                    "ContinuationMapping.NotFound",
                    $"No static continuation mapping found for ref '{mappingRef}'. " +
                    "Ensure the mapping is present in configuration.")));
    }
}
