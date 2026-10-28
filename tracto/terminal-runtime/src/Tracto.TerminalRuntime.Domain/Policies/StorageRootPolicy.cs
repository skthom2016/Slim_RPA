namespace Tracto.TerminalRuntime.Domain.Policies;

/// <summary>
/// Defines the list of approved storage root directories under which output files
/// and artifacts may be written or read. Any path that does not canonicalize to a
/// subtree of one of these roots is rejected with errorCategory=POLICY_DENIED.
/// </summary>
public sealed record StorageRootPolicy(string[] ApprovedRoots)
{
    /// <summary>A policy that approves no roots — used for testing or strict defaults.</summary>
    public static StorageRootPolicy Deny { get; } = new([]);
}
