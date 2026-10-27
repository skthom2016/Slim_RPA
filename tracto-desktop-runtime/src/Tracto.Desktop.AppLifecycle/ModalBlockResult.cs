namespace Tracto.Desktop.AppLifecycle;

/// <summary>
/// Result of a modal-blocking check performed by <see cref="ModalBlocker"/>.
/// When <see cref="IsBlocked"/> is <c>true</c> the caller MUST withhold the pending action.
/// </summary>
/// <param name="IsBlocked">
/// <c>true</c> when an unexpected window is positioned above the target window, blocking input.
/// </param>
/// <param name="ModalTitle">
/// Sanitized title of the blocking window, or <c>null</c> when <see cref="IsBlocked"/> is <c>false</c>.
/// Secret values are never included in this field.
/// </param>
/// <param name="Evidence">
/// Human-readable description of the blocking condition for diagnostics.
/// Must not contain any secret input or step variable plaintext.
/// </param>
public sealed record ModalBlockResult(bool IsBlocked, string? ModalTitle, string Evidence);
