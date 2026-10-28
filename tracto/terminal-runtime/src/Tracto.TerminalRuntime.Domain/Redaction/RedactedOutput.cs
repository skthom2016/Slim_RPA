namespace Tracto.TerminalRuntime.Domain.Redaction;

/// <summary>
/// The result of running a raw output string through the redaction pipeline.
/// </summary>
/// <param name="Content">
///   The sanitised text. All secret token values and any heuristic-matched
///   secret-like strings have been replaced with "***REDACTED***".
/// </param>
/// <param name="WasRedacted">
///   <see langword="true"/> when at least one substitution was made.
/// </param>
public sealed record RedactedOutput(string Content, bool WasRedacted);
