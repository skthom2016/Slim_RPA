namespace Tracto.Designer.Core.Credentials;

using System.Collections.Concurrent;

public sealed class InMemoryCredentialVault : ICredentialVault
{
    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset Stored)> _store =
        new(StringComparer.OrdinalIgnoreCase);

    public void StoreCredential(string name, string value) =>
        _store[name] = (value, DateTimeOffset.UtcNow);

    public string? ResolveCredential(string name) =>
        _store.TryGetValue(name, out var entry) ? entry.Value : null;

    public void RemoveCredential(string name) => _store.TryRemove(name, out _);

    public IReadOnlyList<CredentialEntry> ListCredentials() =>
        [.. _store
            .Select(kv => new CredentialEntry(kv.Key, Obfuscate(kv.Value.Value), kv.Value.Stored))
            .OrderBy(e => e.Name)];

    private static string Obfuscate(string v) =>
        v.Length <= 4 ? new string('*', v.Length) : new string('*', v.Length - 4) + v[^4..];
}
