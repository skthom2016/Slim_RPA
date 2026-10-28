using System.Collections.Concurrent;

namespace TractoRuntime.Browser.Mode;

/// <summary>
/// In-process implementation of <see cref="IForegroundInputOwner"/>.
/// Uses a process-wide lock to ensure only one headed worker holds foreground-input
/// ownership per Windows session at any given time.
/// Cross-process enforcement (between Browser and Desktop workers) is delegated to
/// the host-level Supervisor in isolated-host deployments (BRW-US-002 AC#5) and to
/// the Orchestrator lease policy in connected mode.
/// BRW-US-002 AC#2 (exclusive ownership required for headed), AC#3 (second attempt rejected).
/// </summary>
public sealed class ForegroundInputOwner : IForegroundInputOwner
{
    // sessionId -> ownerRunId; static so all workers in the same process share the state.
    private static readonly ConcurrentDictionary<string, string> s_sessionOwners =
        new(StringComparer.OrdinalIgnoreCase);

    // Guards atomic check-and-set across concurrent lease acquisitions.
    private static readonly SemaphoreSlim s_stateLock = new(1, 1);

    public string? GetCurrentOwner(string sessionId)
        => s_sessionOwners.TryGetValue(sessionId, out var owner) ? owner : null;

    public async Task<ForegroundOwnershipResult> TryAcquireAsync(
        string runId,
        string sessionId,
        CancellationToken ct)
    {
        await s_stateLock.WaitAsync(ct);
        try
        {
            if (s_sessionOwners.TryGetValue(sessionId, out var conflicting))
            {
                // Session already owned by another run — reject (AC#3).
                return new ForegroundOwnershipResult
                {
                    Acquired = false,
                    ConflictingRunId = conflicting,
                    SessionId = sessionId
                };
            }

            s_sessionOwners[sessionId] = runId;
            return new ForegroundOwnershipResult
            {
                Acquired = true,
                SessionId = sessionId
            };
        }
        finally
        {
            s_stateLock.Release();
        }
    }

    public async Task ReleaseAsync(string runId, string sessionId, CancellationToken ct)
    {
        await s_stateLock.WaitAsync(ct);
        try
        {
            // Only remove when the caller is the actual owner; ignore if already released.
            s_sessionOwners.TryRemove(new KeyValuePair<string, string>(sessionId, runId));
        }
        finally
        {
            s_stateLock.Release();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
