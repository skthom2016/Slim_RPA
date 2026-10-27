#nullable enable

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Allowlist of registered serviceRef keys approved for invocation by the Desktop Runtime.
/// Only serviceRef values present in the registry may be invoked; all others trigger a policy violation.
/// </summary>
public interface IServiceRegistry
{
    /// <summary>Returns true when <paramref name="serviceRef"/> is registered and approved.</summary>
    bool IsRegistered(string serviceRef);

    /// <summary>Returns all registered serviceRef keys.</summary>
    IReadOnlyCollection<string> RegisteredKeys { get; }
}
