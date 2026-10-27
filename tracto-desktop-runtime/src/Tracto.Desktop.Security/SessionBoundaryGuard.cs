using Tracto.Desktop.Element;
using Tracto.Desktop.Session;

namespace Tracto.Desktop.Security;

/// <summary>
/// Utility guard that enforces interactive-session policy.
/// Throw <see cref="DesktopActionError"/> with POLICY_DENIED when the session
/// is not interactive; a no-op otherwise.
/// </summary>
public static class SessionBoundaryGuard
{
    /// <summary>
    /// Throws <see cref="DesktopActionError"/> with code <c>POLICY_DENIED</c>
    /// if the current session state is not <see cref="SessionState.Interactive"/>.
    /// </summary>
    /// <param name="provider">The session state provider to query.</param>
    /// <exception cref="DesktopActionError">Thrown when the session is not interactive.</exception>
    public static void ThrowIfNotInteractive(ISessionStateProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var state = provider.GetCurrentState();
        if (state != SessionState.Interactive)
        {
            throw new DesktopActionError(
                "POLICY_DENIED",
                $"Secret operations require an interactive session; current state is {state}.");
        }
    }
}
