#nullable enable

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Tracto.Contract.Orchestrator
{
    /// <summary>
    /// Enforces C4 rule: at most one concurrent interactive lease per interactiveSessionId.
    /// All operations are thread-safe via ConcurrentDictionary.
    /// </summary>
    public sealed class InteractiveLeaseEnforcer
    {
        private readonly ConcurrentDictionary<string, string> _held =
            new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Returns true and records leaseId when sessionId is not currently held.
        /// Returns false when sessionId is already held by a different leaseId.
        /// </summary>
        public bool TryAcquire(string sessionId, string leaseId)
        {
            return _held.TryAdd(sessionId, leaseId);
        }

        /// <summary>
        /// Releases the lease for sessionId ONLY if current leaseId matches.
        /// No-ops silently when leaseId does not match or session is not held.
        /// </summary>
        public void Release(string sessionId, string leaseId)
        {
            _held.TryRemove(new KeyValuePair<string, string>(sessionId, leaseId));
        }

        /// <summary>
        /// True when sessionId is currently held by any lease.
        /// </summary>
        public bool IsHeld(string sessionId)
        {
            return _held.ContainsKey(sessionId);
        }
    }
}
