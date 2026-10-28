using Tracto.TerminalRuntime.Domain.Commands;

namespace Tracto.TerminalRuntime.Application.Commands;

/// <summary>
/// Read-only view of the pre-approved command registry.
/// Implemented by Infrastructure; consumed by Application handlers.
/// </summary>
public interface ICommandRegistry
{
    /// <summary>
    /// Returns the entry whose <c>CommandRef</c> matches <paramref name="commandRef"/>
    /// (case-insensitive), or <c>null</c> if no match is found.
    /// </summary>
    CommandRegistryEntry? TryGet(string commandRef);
}
