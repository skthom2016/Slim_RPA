using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Bindings;

namespace Tracto.TerminalRuntime.Application.Interfaces;

/// <summary>
/// Resolves a step's <see cref="EnvironmentBinding"/> list into a concrete set of
/// environment variables to be passed to the child process.
///
/// The host ambient environment is never forwarded. Secret references are resolved
/// through <see cref="ISecretStore"/>; their values are marked in
/// <see cref="EnvironmentBuildResult.SecretVariableNames"/> so that callers can
/// redact them from audit records, telemetry, and Orchestrator payloads.
/// </summary>
public interface IEnvironmentBuilder
{
    /// <summary>
    /// Builds the environment dictionary from the supplied bindings.
    /// </summary>
    /// <param name="bindings">
    ///   The explicit environment declarations from the step definition.
    ///   Only these are forwarded to the child process.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<EnvironmentBuildResult, Error>> BuildAsync(
        IReadOnlyList<EnvironmentBinding> bindings,
        CancellationToken ct = default);
}
