namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Options for launching an application via <see cref="IAppLifecycleManager.LaunchAsync"/>.
/// The executable must be on the policy allowlist before launch is attempted.
/// </summary>
/// <param name="ExecutablePath">Full path to the executable or shortcut.</param>
/// <param name="Args">Command-line arguments to pass to the process.</param>
/// <param name="LaunchTimeoutMs">Maximum time in milliseconds to wait for the process to enter an idle state after start.</param>
public sealed record AppLaunchOptions(
    string ExecutablePath,
    string[] Args,
    int LaunchTimeoutMs);
