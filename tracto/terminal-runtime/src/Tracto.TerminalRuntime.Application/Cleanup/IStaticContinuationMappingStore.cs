using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Cleanup;

namespace Tracto.TerminalRuntime.Application.Cleanup;

/// <summary>
/// Read-only store for statically-configured continuation mappings.
///
/// Mappings are set at deploy / configuration time and never written by runtime
/// code, ensuring that dynamic scriptRefs from callback payloads can be detected
/// and refused (CEC v1.0.1 AC-4).
/// </summary>
public interface IStaticContinuationMappingStore
{
    /// <summary>
    /// Returns the mapping for <paramref name="mappingRef"/>, or a failure if none exists.
    /// </summary>
    Task<Result<StaticContinuationMapping, Error>> GetMappingAsync(
        string mappingRef,
        CancellationToken ct = default);
}
