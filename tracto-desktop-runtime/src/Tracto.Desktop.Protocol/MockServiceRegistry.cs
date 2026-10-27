#nullable enable

namespace Tracto.Desktop.Protocol;

/// <summary>
/// In-process mock implementation of <see cref="IServiceRegistry"/> used in tests and local harnesses.
/// Accepts a fixed set of approved serviceRef keys at construction time.
/// </summary>
public sealed class MockServiceRegistry : IServiceRegistry
{
    private readonly HashSet<string> _keys;

    /// <summary>
    /// Creates a registry with the given approved serviceRef keys.
    /// </summary>
    /// <param name="approvedServiceRefs">Approved serviceRef strings; comparison is ordinal case-sensitive.</param>
    public MockServiceRegistry(IEnumerable<string> approvedServiceRefs)
    {
        ArgumentNullException.ThrowIfNull(approvedServiceRefs);
        _keys = new HashSet<string>(approvedServiceRefs, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public bool IsRegistered(string serviceRef) => _keys.Contains(serviceRef);

    /// <inheritdoc />
    public IReadOnlyCollection<string> RegisteredKeys => _keys;
}
