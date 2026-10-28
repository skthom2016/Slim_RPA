using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Domain.Audit;

namespace Tracto.TerminalRuntime.Application.Audit;

// ---------------------------------------------------------------------------
// Command
// ---------------------------------------------------------------------------

/// <summary>
/// Carries the inputs required to audit a command execution and to enforce
/// retry eligibility based on the command's <see cref="IdempotencyClass"/>
/// (TER-US-010, AC-1 through AC-4).
///
/// CEC v1.0.1 invariant #4: <see cref="ArgNames"/> must contain ONLY argument
/// names — never raw secret values or plaintext credentials.
/// </summary>
/// <param name="CommandRef">Logical template identifier for the command being audited.</param>
/// <param name="ArgNames">Names of the arguments passed (values are excluded).</param>
/// <param name="ArtifactHashes">SHA-256 digests of approved artifacts used by this invocation.</param>
/// <param name="RunId">Run identifier.</param>
/// <param name="AttemptId">Attempt fence identifier.</param>
/// <param name="IdentityRef">Opaque reference to the effective identity (NOT a plaintext secret).</param>
/// <param name="IdempotencyClass">Replay-safety classification for the command step.</param>
/// <param name="IsRetryAttempt">
///   True when this execution is a retry of a previously-attempted step.
///   The handler enforces that <see cref="IdempotencyClass.Irreversible"/> steps
///   are NEVER retried regardless of this flag.
/// </param>
/// <param name="RetryCount">
///   How many times the step has already been attempted (0 = first attempt).
/// </param>
/// <param name="MaxRetries">
///   Upper bound on automatic retries.  Honoured only for
///   <see cref="IdempotencyClass.ReadOnly"/> and <see cref="IdempotencyClass.Idempotent"/> steps.
/// </param>
public sealed record AuditCommandExecutionCommand(
    string CommandRef,
    string[] ArgNames,
    string[] ArtifactHashes,
    string RunId,
    string AttemptId,
    string IdentityRef,
    IdempotencyClass IdempotencyClass,
    bool IsRetryAttempt,
    int RetryCount,
    int MaxRetries);

// ---------------------------------------------------------------------------
// Result payload
// ---------------------------------------------------------------------------

/// <summary>Result returned when the audit and eligibility check succeed.</summary>
/// <param name="AuditRecord">The persisted audit record (no secret values).</param>
/// <param name="RetryPermitted">
///   True when the step is eligible for an automatic retry.
///   Always false for <see cref="IdempotencyClass.Irreversible"/> steps.
/// </param>
public sealed record AuditCommandExecutionResult(
    CommandAuditRecord AuditRecord,
    bool RetryPermitted);

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

/// <summary>
/// Audits a command execution and enforces the retry-eligibility rules mandated
/// by CEC v1.0.1 invariant #3 and TER-US-010.
///
/// Rules:
/// <list type="bullet">
///   <item>
///     <see cref="IdempotencyClass.Irreversible"/> — never retried after timeout
///     or lost acknowledgement.  <c>RetryPermitted</c> is always <c>false</c>.
///   </item>
///   <item>
///     <see cref="IdempotencyClass.ReadOnly"/> and
///     <see cref="IdempotencyClass.Idempotent"/> — may be retried up to
///     <c>MaxRetries</c> on transient failure.
///   </item>
/// </list>
///
/// No process-launching logic lives here.
/// </summary>
public sealed class AuditCommandExecutionHandler(
    ICommandAuditLog auditLog,
    IClock clock)
{
    /// <summary>
    /// Validates retry eligibility, writes the audit record, and returns the result.
    /// </summary>
    public async Task<Result<AuditCommandExecutionResult, Error>> HandleAsync(
        AuditCommandExecutionCommand command,
        CancellationToken ct = default)
    {
        // ── AC-1 / CEC #3: Irreversible steps must never be auto-replayed ──────
        //
        // Defense-in-depth: query the audit log first so that a buggy or restarted
        // caller cannot bypass the block by presenting IsRetryAttempt=false on a
        // genuine re-dispatch of an Irreversible command (the "ambiguous timeout /
        // lost acknowledgement" scenario from CEC v1.0.1 invariant #3).
        if (command.IdempotencyClass == IdempotencyClass.Irreversible)
        {
            var priorResult = await auditLog.GetByRunIdAsync(command.RunId, ct).ConfigureAwait(false);
            if (priorResult.IsSuccess)
            {
                bool priorIrreversibleExists = priorResult.Value.Any(
                    r => r.IdempotencyClass == IdempotencyClass.Irreversible
                         && string.Equals(r.CommandRef, command.CommandRef, StringComparison.Ordinal));

                if (priorIrreversibleExists)
                {
                    return Result.Fail<AuditCommandExecutionResult>(
                        Error.PolicyDenied(
                            "AUDIT_001",
                            $"Command '{command.CommandRef}' is classified Irreversible and cannot be automatically retried.",
                            detailCode: "terminal.irreversibleCommandRetryBlocked"));
                }
            }
            // If the log query itself failed, fall through to the fast-path flag check
            // so a transient storage error does not silently allow a replay — the flag
            // check below will still block an explicit retry attempt.
        }

        // Fast-path flag check: remains correct for the non-ambiguous case and
        // catches the scenario where the log query could not be executed.
        if (command.IdempotencyClass == IdempotencyClass.Irreversible && command.IsRetryAttempt)
        {
            return Result.Fail<AuditCommandExecutionResult>(
                Error.PolicyDenied(
                    "AUDIT_001",
                    $"Command '{command.CommandRef}' is classified Irreversible and cannot be automatically retried.",
                    detailCode: "terminal.irreversibleCommandRetryBlocked"));
        }

        // ── Determine retry eligibility ──────────────────────────────────────
        bool retryPermitted = command.IdempotencyClass != IdempotencyClass.Irreversible
                              && command.RetryCount < command.MaxRetries;

        // ── Build and persist audit record (no secret values) ────────────────
        var record = new CommandAuditRecord(
            CommandRef:       command.CommandRef,
            ArgNames:         command.ArgNames,
            ArtifactHashes:   command.ArtifactHashes,
            RunId:            command.RunId,
            AttemptId:        command.AttemptId,
            IdentityRef:      command.IdentityRef,
            ExecutedAt:       clock.UtcNow,
            IdempotencyClass: command.IdempotencyClass);

        var appendResult = await auditLog.AppendAsync(record, ct).ConfigureAwait(false);
        if (appendResult.IsFailure)
            return Result.Fail<AuditCommandExecutionResult>(appendResult.Error);

        return Result.Ok(new AuditCommandExecutionResult(record, retryPermitted));
    }
}
