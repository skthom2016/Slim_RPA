namespace Tracto.TerminalRuntime.Domain.Policies;

/// <summary>
/// Defines the list of approved root directories from which working directory
/// references (cwdRef) may be resolved. Any path that does not canonicalize to
/// a subtree of one of these roots is rejected.
/// </summary>
public sealed record WorkingDirPolicy(string[] ApprovedRoots)
{
    /// <summary>A policy that approves no roots — used for testing / strict defaults.</summary>
    public static WorkingDirPolicy Deny { get; } = new([]);
}
