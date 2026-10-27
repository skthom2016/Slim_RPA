namespace Tracto.Desktop.Recovery;

/// <summary>
/// Result of a foreground-window ownership check.
/// </summary>
/// <param name="IsStolen">True when the foreground window differs from the expected window handle.</param>
/// <param name="StealingWindowTitle">
/// Sanitised title of the window that has taken foreground focus, or <see langword="null"/> when
/// <see cref="IsStolen"/> is <see langword="false"/>.
/// </param>
public sealed record FocusTheftResult(bool IsStolen, string? StealingWindowTitle);
