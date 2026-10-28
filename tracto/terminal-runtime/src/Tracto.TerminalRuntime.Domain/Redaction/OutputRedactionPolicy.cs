namespace Tracto.TerminalRuntime.Domain.Redaction;

/// <summary>
/// Immutable policy that governs how output is sanitised before any persistence,
/// forwarding to the Orchestrator, or inclusion in evidence artifacts.
/// </summary>
/// <param name="Mode">
///   The redaction mode to apply. Defaults to <see cref="RedactionMode.MaskAll"/>.
///   Callers may not supply a value that disables redaction entirely.
/// </param>
/// <param name="ActiveSecretTokens">
///   The resolved secret values that must never appear in output.
///   These are handled in memory and are never persisted in the policy record itself.
/// </param>
public sealed record OutputRedactionPolicy(
    RedactionMode Mode,
    IReadOnlyList<string> ActiveSecretTokens)
{
    /// <summary>
    /// Constructs the default policy (MaskAll, no pre-resolved tokens).
    /// Callers must call <see cref="WithTokens"/> before use in production.
    /// </summary>
    public static OutputRedactionPolicy Default { get; } =
        new(RedactionMode.MaskAll, Array.Empty<string>());

    /// <summary>
    /// Returns a new policy with the same mode but the supplied resolved tokens.
    /// </summary>
    public OutputRedactionPolicy WithTokens(IReadOnlyList<string> tokens) =>
        this with { ActiveSecretTokens = tokens };
}
