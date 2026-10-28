using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain.Audit;

namespace Tracto.TerminalRuntime.Application.Audit;

/// <summary>
/// Append-only audit log for command executions.
///
/// Implementations MUST NOT write raw secret parameter values to any storage medium
/// (CEC v1.0.1 invariant #4).
/// </summary>
public interface ICommandAuditLog
{
    /// <summary>
    /// Appends a <see cref="CommandAuditRecord"/> to the audit store for the given run.
    /// The operation must be idempotent for identical (RunId, AttemptId) pairs.
    /// </summary>
    Task<Result<bool, Error>> AppendAsync(
        CommandAuditRecord record,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all audit records persisted for the given <paramref name="runId"/>,
    /// in chronological order.
    /// </summary>
    Task<Result<IReadOnlyList<CommandAuditRecord>, Error>> GetByRunIdAsync(
        string runId,
        CancellationToken ct = default);
}
