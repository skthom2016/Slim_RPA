using System.Security.Cryptography;
using System.Text;
using Tracto.Desktop.Input;
using Tracto.Desktop.Session;

namespace Tracto.Desktop.Security;

/// <summary>
/// Resolves a <see cref="SecretRef"/> by fetching an encrypted blob from
/// <see cref="ICredentialStore"/> and decrypting it with DPAPI
/// (scoped to the current user).  The plaintext is placed in a
/// <see cref="ClearableBuffer"/> and NEVER materialised as a managed string.
/// </summary>
public sealed class DpapiSecretResolver : ISecretResolver
{
    private readonly ICredentialStore _store;
    private readonly ISessionStateProvider _sessionState;

    public DpapiSecretResolver(ICredentialStore store, ISessionStateProvider sessionState)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sessionState = sessionState ?? throw new ArgumentNullException(nameof(sessionState));
    }

    /// <inheritdoc/>
    public Task<ClearableBuffer> ResolveAsync(SecretRef secretRef, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(secretRef);
        ct.ThrowIfCancellationRequested();

        // Policy gate: must be interactive BEFORE any decryption is attempted.
        SessionBoundaryGuard.ThrowIfNotInteractive(_sessionState);

        var encryptedBlob = _store.GetEncryptedBlob(secretRef.Key);

        // DPAPI decryption — plaintext bytes never assigned to a string.
        var plaintextBytes = ProtectedData.Unprotect(encryptedBlob, null, DataProtectionScope.CurrentUser);

        try
        {
            // Decode bytes to chars and write directly into a ClearableBuffer.
            var charCount = plaintextBytes.Length > 0
                ? Encoding.UTF8.GetCharCount(plaintextBytes)
                : 1;
            var buffer = new ClearableBuffer(charCount > 0 ? charCount : 1);

            // Decode directly into the span — no intermediate string.
            Encoding.UTF8.GetChars(plaintextBytes.AsSpan(), buffer.Span);

            return Task.FromResult(buffer);
        }
        finally
        {
            // Zero the DPAPI plaintext byte array immediately after use.
            Array.Clear(plaintextBytes, 0, plaintextBytes.Length);
        }
    }
}
