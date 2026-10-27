namespace Tracto.Desktop.Recovery;

/// <summary>
/// Detects unexpected process exit and Windows Error Reporting dialogs for a given process.
/// </summary>
public interface ICrashDetector
{
    /// <summary>
    /// Inspects the process identified by <paramref name="processId"/> and returns a
    /// <see cref="CrashResult"/> describing its exit and WER status.
    /// </summary>
    /// <param name="processId">The OS process identifier to inspect.</param>
    CrashResult DetectCrash(int processId);
}
