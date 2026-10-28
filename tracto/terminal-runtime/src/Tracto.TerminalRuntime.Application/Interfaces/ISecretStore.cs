using Tracto.TerminalRuntime.Application.Common;

namespace Tracto.TerminalRuntime.Application.Interfaces;

/// <summary>
/// Resolves opaque secret references to their runtime values.
/// Implementations MUST ensure that resolved values are never written to logs,
/// audit records, evidence payloads, or Orchestrator messages.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Retrieves the secret value for the given <paramref name="secretRef"/>.
    /// </summary>
    /// <param name="secretRef">
    ///   The opaque reference declared in the step's <c>environmentRefs</c>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///   The resolved secret value on success, or an error
    ///   when the reference cannot be resolved.
    /// </returns>
    Task<Result<string, Error>> GetSecretAsync(string secretRef, CancellationToken ct = default);
}
