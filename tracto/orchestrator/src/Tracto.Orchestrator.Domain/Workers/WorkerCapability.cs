#nullable enable
namespace Tracto.Orchestrator.Domain.Workers;

/// <summary>
/// Value object describing what a worker can execute.
/// Domain layer — zero EF/ASP dependencies.
/// CapabilityDigest is an integrity hint supplied by the caller; the orchestrator
/// validates it against the hash of the serialised ActionNames + RuntimeType,
/// but a mismatch is logged as a warning only — registration still succeeds.
/// </summary>
public sealed class WorkerCapability
{
    /// <summary>Ordered set of action names the worker can handle (e.g. "ClickAction", "TypeAction").</summary>
    public IReadOnlyList<string> ActionNames { get; }

    /// <summary>Runtime type tag that matches the owning WorkerRecord.RuntimeType.</summary>
    public string RuntimeType { get; }

    /// <summary>
    /// Optional SHA-256 hex digest over the canonical JSON of {ActionNames, RuntimeType}.
    /// Provided by the worker as an integrity hint; validated by the application handler.
    /// Null means the caller did not supply a digest.
    /// </summary>
    public string? CapabilityDigest { get; }

    public WorkerCapability(
        IEnumerable<string> actionNames,
        string runtimeType,
        string? capabilityDigest = null)
    {
        ArgumentNullException.ThrowIfNull(actionNames);
        if (string.IsNullOrWhiteSpace(runtimeType))
            throw new ArgumentException("RuntimeType is required.", nameof(runtimeType));

        // Normalize to sorted order so that capabilities with the same action set but
        // different caller-supplied ordering compare equal and round-trip identically.
        // ComputeDigest in the application layer also sorts before hashing; keeping the
        // stored list sorted keeps those two representations consistent.
        ActionNames       = actionNames.OrderBy(x => x, StringComparer.Ordinal).ToList().AsReadOnly();
        RuntimeType       = runtimeType;
        CapabilityDigest  = capabilityDigest;
    }

    // ── Equality (value semantics) ────────────────────────────────────────────
    // ActionNames is stored in canonical (sorted) order so SequenceEqual is correct
    // for set comparison after both instances have been normalised by the constructor.

    public override bool Equals(object? obj) =>
        obj is WorkerCapability other
        && RuntimeType == other.RuntimeType
        && CapabilityDigest == other.CapabilityDigest
        && ActionNames.SequenceEqual(other.ActionNames, StringComparer.Ordinal);

    public override int GetHashCode()
    {
        var hash = HashCode.Combine(RuntimeType, CapabilityDigest);
        foreach (var name in ActionNames)
            hash = HashCode.Combine(hash, name);
        return hash;
    }

    public static bool operator ==(WorkerCapability? left, WorkerCapability? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(WorkerCapability? left, WorkerCapability? right) =>
        !(left == right);

    public override string ToString() =>
        $"WorkerCapability(RuntimeType={RuntimeType}, Actions=[{string.Join(", ", ActionNames)}], Digest={CapabilityDigest ?? "none"})";
}
