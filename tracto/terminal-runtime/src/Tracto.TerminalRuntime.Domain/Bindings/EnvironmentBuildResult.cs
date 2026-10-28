namespace Tracto.TerminalRuntime.Domain.Bindings;

/// <summary>
/// The fully resolved environment for a child process.
/// </summary>
/// <param name="Variables">
///   The complete set of environment variables to pass to the child process.
///   This includes both plain-text and secret-resolved values.
/// </param>
/// <param name="SecretVariableNames">
///   The names of variables whose values originated from a <see cref="EnvironmentBinding.SecretRef"/>.
///   Callers producing audit records, evidence payloads, or telemetry MUST exclude
///   any key present in this set from their output.
/// </param>
public sealed record EnvironmentBuildResult(
    IReadOnlyDictionary<string, string> Variables,
    IReadOnlySet<string> SecretVariableNames);
