namespace Tracto.TerminalRuntime.Application;

/// <summary>
/// Deployment-time policy that constrains which remote hosts a Terminal worker
/// may target. Any host NOT in AllowedRemoteHosts must be rejected with
/// errorCategory = POLICY_DENIED before any network connection is attempted.
/// </summary>
public sealed record RemoteContextPolicy(IReadOnlyList<string> AllowedRemoteHosts)
{
    public static RemoteContextPolicy AllowNone() =>
        new(Array.Empty<string>());

    public bool IsHostAllowed(string host) =>
        AllowedRemoteHosts.Any(h =>
            string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
}
