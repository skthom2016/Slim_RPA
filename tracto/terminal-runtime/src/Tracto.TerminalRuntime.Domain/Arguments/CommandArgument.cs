namespace Tracto.TerminalRuntime.Domain.Arguments;

/// <summary>
/// A single typed argument to be passed to a shell process.
/// The value is treated as opaque data — it must NEVER be string-concatenated
/// into a shell command line; only the ArgumentList typed API may receive it.
/// </summary>
/// <param name="Name">Logical parameter name (schema identifier).</param>
/// <param name="Value">The raw argument value supplied at run time.</param>
/// <param name="MaxLength">Optional declared maximum byte-length from the schema.</param>
public sealed record CommandArgument(
    string Name,
    string Value,
    int? MaxLength = null);
