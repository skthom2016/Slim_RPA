using System.Text;

namespace Tracto.Desktop.Security;

/// <summary>
/// Configurable in-memory credential store for testing and development.
/// Returns mock encrypted blobs keyed by name.
/// </summary>
public sealed class MockCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, byte[]> _blobs;

    /// <summary>
    /// Initialises a mock store with an empty set of credentials.
    /// </summary>
    public MockCredentialStore()
    {
        _blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Initialises a mock store with the provided credential map.
    /// </summary>
    /// <param name="blobs">A map of key → encrypted blob to pre-populate the store.</param>
    public MockCredentialStore(IReadOnlyDictionary<string, byte[]> blobs)
    {
        _blobs = new Dictionary<string, byte[]>(blobs, StringComparer.Ordinal);
    }

    /// <summary>Adds or replaces an entry in the mock store.</summary>
    public void SetBlob(string key, byte[] encryptedBlob) => _blobs[key] = encryptedBlob;

    /// <summary>Adds a plaintext value by UTF-8-encoding it (for testing only).</summary>
    public void SetPlaintext(string key, string plaintext) =>
        _blobs[key] = Encoding.UTF8.GetBytes(plaintext);

    /// <inheritdoc/>
    public byte[] GetEncryptedBlob(string key)
    {
        if (_blobs.TryGetValue(key, out var blob))
            return blob;
        throw new KeyNotFoundException($"No credential found for key '{key}'.");
    }
}
