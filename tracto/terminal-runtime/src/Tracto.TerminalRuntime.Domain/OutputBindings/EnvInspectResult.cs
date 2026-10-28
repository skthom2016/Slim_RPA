namespace Tracto.TerminalRuntime.Domain.OutputBindings;

/// <summary>
/// Result of a <c>terminal.env.inspect</c> action.
/// When the resolved value matches an active secret reference it is automatically
/// redacted from evidence and <see cref="WasRedacted"/> is set to true.
/// </summary>
/// <param name="Name">The allowlisted environment variable name that was inspected.</param>
/// <param name="Value">
///   The resolved value, or <c>[REDACTED]</c> when <see cref="WasRedacted"/> is true.
///   Never contains a plaintext secret in evidence or journal records.
/// </param>
/// <param name="WasRedacted">True when the resolved value matched an active secretRef and was replaced by a redaction marker.</param>
public sealed record EnvInspectResult(string Name, string Value, bool WasRedacted);
