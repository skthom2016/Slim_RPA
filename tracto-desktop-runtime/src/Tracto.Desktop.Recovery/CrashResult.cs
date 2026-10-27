namespace Tracto.Desktop.Recovery;

/// <summary>
/// Result of a crash-detection check on a managed application process.
/// </summary>
/// <param name="IsCrashed">True when the target process has exited or cannot be found.</param>
/// <param name="ExitCode">The process exit code when available; null if the process is still running or the code cannot be retrieved.</param>
/// <param name="WerDialogDetected">True when a Windows Error Reporting (WerFault) dialog was observed at detection time.</param>
public sealed record CrashResult(bool IsCrashed, int? ExitCode, bool WerDialogDetected);
