#nullable enable

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Validates that all action namespaces in a job are compatible with the Desktop Runtime's capabilities.
/// Rejects jobs that contain browser.* or terminal.* actions before AcceptLease is called.
/// </summary>
public static class CapabilityNamespaceGuard
{
    private static readonly string[] DisallowedPrefixes = ["browser.", "terminal."];

    /// <summary>
    /// Validates that none of the supplied action namespaces target a non-desktop environment.
    /// </summary>
    /// <param name="actionNamespaces">Action namespace strings from the job script.</param>
    /// <exception cref="DesktopActionError">
    /// Thrown with errorCode "ENVIRONMENT" when any action namespace starts with a disallowed prefix.
    /// </exception>
    public static void ValidateJob(string[] actionNamespaces)
    {
        ArgumentNullException.ThrowIfNull(actionNamespaces);

        foreach (var ns in actionNamespaces)
        {
            foreach (var prefix in DisallowedPrefixes)
            {
                if (ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DesktopActionError(
                        "ENVIRONMENT",
                        $"Action namespace '{ns}' is not supported by the Desktop Runtime (prefix '{prefix}' is disallowed).");
                }
            }
        }
    }
}
