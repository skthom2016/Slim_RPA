namespace Tracto.TerminalRuntime.Domain.Bindings;

/// <summary>
/// Declares a single environment variable to be injected into the child process.
/// Exactly one of <see cref="Value"/> or <see cref="SecretRef"/> must be set.
/// </summary>
/// <param name="Name">The environment variable name (e.g. "MY_API_BASE_URL").</param>
/// <param name="Value">
///   A plain-text literal value. Must not be used for secrets — use <see cref="SecretRef"/> instead.
/// </param>
/// <param name="SecretRef">
///   An opaque reference to a secret stored in the secret store. The resolved value
///   is injected into the child process but is never written to logs, audit records,
///   or Orchestrator payloads.
/// </param>
public sealed record EnvironmentBinding(
    string Name,
    string? Value = null,
    string? SecretRef = null);
