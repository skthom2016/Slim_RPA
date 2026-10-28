using System.Collections.Concurrent;

namespace TractoRuntime.Browser.Lease;

/// <summary>
/// CEC-HOST-002: Enforces at most one active job lease per registered worker at any time.
/// A second <see cref="TryAcquire"/> for the same workerId fails until the first lease is released.
/// Thread-safe via ConcurrentDictionary.
/// </summary>
public sealed class LeaseFence
{
    private readonly ConcurrentDictionary<string, string> _activeLeases = new();

    /// <summary>
    /// Attempts to register <paramref name="leaseToken"/> as the active lease for <paramref name="workerId"/>.
    /// Returns <c>true</c> if the fence was acquired; <c>false</c> if a lease is already active.
    /// </summary>
    public bool TryAcquire(string workerId, string leaseToken)
        => _activeLeases.TryAdd(workerId, leaseToken);

    /// <summary>Releases the active lease for <paramref name="workerId"/>.</summary>
    public void Release(string workerId)
        => _activeLeases.TryRemove(workerId, out _);

    /// <summary>Returns <c>true</c> when a lease is currently held for <paramref name="workerId"/>.</summary>
    public bool HasActiveLease(string workerId)
        => _activeLeases.ContainsKey(workerId);

    /// <summary>Returns the active lease token for <paramref name="workerId"/>, or <c>null</c>.</summary>
    public string? GetActiveLeaseToken(string workerId)
        => _activeLeases.TryGetValue(workerId, out var token) ? token : null;
}
