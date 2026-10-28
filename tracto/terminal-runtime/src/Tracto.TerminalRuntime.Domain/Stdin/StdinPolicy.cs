namespace Tracto.TerminalRuntime.Domain.Stdin;

/// <summary>
/// Governs bounds and timing for interactive stdin writes.
///
/// <list type="bullet">
///   <item><see cref="MaxBytes"/> — maximum UTF-8-encoded byte count of a single stdin payload (AC-5).</item>
///   <item><see cref="PromptTimeoutMs"/> — milliseconds the runtime waits for an allowlisted prompt
///         to appear on stdout before treating the step as timed-out (AC-2, AC-3).</item>
/// </list>
/// </summary>
/// <param name="MaxBytes">
///   Maximum size in bytes of a single stdin payload.  Defaults to 65 536 (64 KiB).
/// </param>
/// <param name="PromptTimeoutMs">
///   Maximum time in milliseconds to wait for a matching prompt on stdout.
///   Defaults to 30 000 ms (30 s).
/// </param>
public sealed record StdinPolicy(
    int MaxBytes = 65_536,
    int PromptTimeoutMs = 30_000);
