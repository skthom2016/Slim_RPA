namespace Tracto.TerminalRuntime.Domain.OutputBindings;

/// <summary>
/// Represents a single named output binding captured from a step's stdout JSON.
/// The <see cref="Value"/> is the raw JSON-serialized string from captured output.
/// The runtime performs zero numeric comparisons or domain decisions on this value.
/// </summary>
/// <param name="Name">The binding name declared in the step's output-bindings configuration.</param>
/// <param name="Value">The raw JSON-serialized value from captured stdout. Never interpreted by the runtime.</param>
public sealed record StepOutputBinding(string Name, string Value);
