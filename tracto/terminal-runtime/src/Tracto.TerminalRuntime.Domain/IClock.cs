namespace Tracto.TerminalRuntime.Domain;

/// <summary>
/// Abstraction over wall-clock time so that tests can control it without
/// monkey-patching static members.
/// </summary>
public interface IClock
{
    /// <summary>Returns the current UTC date and time.</summary>
    DateTimeOffset UtcNow { get; }
}
