namespace Tracto.TerminalRuntime.Domain.Cleanup;

/// <summary>
/// Statically-configured mapping that determines which continuation run
/// (Run B) should be created when an eligible callback is received for a
/// HANDED_OFF source run.
///
/// CEC v1.0.1 AC-4:
/// - The <see cref="ScriptRef"/> is fixed at configuration time; any attempt
///   to substitute a dynamic scriptRef from the callback payload is refused.
/// - Run B is created only after HANDED_OFF state is confirmed for the source run;
///   speculative creation before that state is persisted is forbidden.
/// </summary>
public sealed record StaticContinuationMapping(
    /// <summary>
    /// Unique key that identifies this mapping.
    /// Source runs embed this key so that the callback handler can look up the
    /// correct mapping without trusting the callback payload for the scriptRef.
    /// </summary>
    string MappingRef,

    /// <summary>
    /// The scriptRef that will be used for the continuation run (Run B).
    /// This value is set at deploy time and must not be overridden by the
    /// incoming callback payload.
    /// </summary>
    string ScriptRef,

    /// <summary>Optional human-readable description of this continuation.</summary>
    string? Description = null);
