namespace Tracto.Desktop.Controls;

/// <summary>
/// Identifies a tab in a tab strip by its display name.
/// </summary>
/// <param name="TabName">
/// The accessible name of the tab to select (case-insensitive match).
/// </param>
public sealed record TabSelector(string TabName);
