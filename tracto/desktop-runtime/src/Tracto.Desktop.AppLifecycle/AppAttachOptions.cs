namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Options for attaching to a running application via <see cref="IAppLifecycleManager.AttachAsync"/>.
/// At least one of <see cref="ProcessName"/> or <see cref="ProcessId"/> must be supplied.
/// </summary>
/// <param name="ProcessName">
/// Process image name to search for (case-insensitive, without extension).
/// When both <see cref="ProcessName"/> and <see cref="ProcessId"/> are set the PID is the primary discriminator.
/// </param>
/// <param name="ProcessId">OS process identifier. Uniquely identifies a process when provided.</param>
public sealed record AppAttachOptions(string? ProcessName, int? ProcessId);
