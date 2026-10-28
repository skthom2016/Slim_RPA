namespace Tracto.TerminalRuntime.Domain.Audit;

/// <summary>
/// Immutable audit record for a single command execution attempt.
///
/// CEC v1.0.1 invariant #4: NO raw secret parameter values are stored here.
/// Argument values are represented only as SHA-256 hashes inside
/// <see cref="ArtifactHashes"/>; secret names appear only in <see cref="ArgNames"/>.
/// </summary>
/// <param name="CommandRef">
///   The logical command identifier from the approved registry (e.g. "cmd://export-report").
///   This is the template ID, not the resolved executable path.
/// </param>
/// <param name="ArgNames">
///   Names of the arguments passed to the command (never the values).
///   Secret argument values must not appear here.
/// </param>
/// <param name="ArtifactHashes">
///   SHA-256 hex digests of approved artifact files associated with this invocation.
///   Sufficient to reconstruct which artifact versions were used without exposing content.
/// </param>
/// <param name="RunId">Run identifier for cross-referencing with the run journal.</param>
/// <param name="AttemptId">Attempt fence — ties this record to a specific execution attempt.</param>
/// <param name="IdentityRef">
///   Opaque reference to the effective identity under which the command ran
///   (e.g. a Windows SID or a masked principal name).
///   Must NOT be a plaintext credential.
/// </param>
/// <param name="ExecutedAt">Wall-clock time at which the command was dispatched.</param>
/// <param name="IdempotencyClass">Classification used to decide retry eligibility.</param>
public sealed record CommandAuditRecord(
    string CommandRef,
    string[] ArgNames,
    string[] ArtifactHashes,
    string RunId,
    string AttemptId,
    string IdentityRef,
    DateTimeOffset ExecutedAt,
    IdempotencyClass IdempotencyClass);
