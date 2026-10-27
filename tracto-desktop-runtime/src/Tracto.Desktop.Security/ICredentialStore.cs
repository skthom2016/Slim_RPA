namespace Tracto.Desktop.Security;

/// <summary>
/// Provides access to encrypted credential blobs stored in the OS credential store.
/// </summary>
public interface ICredentialStore
{
    /// <summary>
    /// Returns the encrypted blob for the given key.
    /// </summary>
    /// <param name="key">The credential key name.</param>
    /// <returns>The encrypted byte array for the secret.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when no credential exists for <paramref name="key"/>.</exception>
    byte[] GetEncryptedBlob(string key);
}
