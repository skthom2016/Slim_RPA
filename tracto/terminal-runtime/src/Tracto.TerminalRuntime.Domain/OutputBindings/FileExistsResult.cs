namespace Tracto.TerminalRuntime.Domain.OutputBindings;

/// <summary>
/// Result of a <c>terminal.file.exists</c> action.
/// Path is always canonicalized and validated against the approved storage root
/// before <see cref="Exists"/> is determined.
/// </summary>
/// <param name="Path">The canonicalized, approved path that was checked.</param>
/// <param name="Exists">True when the file exists at <see cref="Path"/>; false otherwise.</param>
public sealed record FileExistsResult(string Path, bool Exists);
