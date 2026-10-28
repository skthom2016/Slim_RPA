using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TractoRuntime.Browser.Session;

/// <summary>
/// AES-256-GCM-backed implementation of <see cref="ISessionStore"/> for BRW-US-014.
///
/// <para><b>Security constraints (AC#2, AC#4, AC#5, BRE-006, WEB-008):</b></para>
/// <list type="bullet">
///   <item>The raw <c>storageState</c> JSON (which may contain live cookies, tokens, and
///         authorization data) is encrypted with AES-256-GCM before being written to disk.
///         The plaintext value is held in memory only for the minimum required duration.</item>
///   <item>The returned <c>sessionRef</c> is an opaque key — a random GUID — that identifies
///         the encrypted blob.  The raw value is never embedded in scripts, traces, or
///         Orchestrator payloads.</item>
///   <item>Log calls use structured logging with no positional format arguments that could
///         expand a sensitive value; the raw JSON is never passed to any logger.</item>
///   <item>Refresh tokens or authorization headers contained in the storageState are encrypted
///         at rest and never appear in traces, telemetry, or log output (AC#5).</item>
///   <item>Each blob gets its own randomly generated 256-bit key stored adjacent to the blob.
///         In a production deployment the per-blob key would be wrapped by a platform secret
///         store (e.g., Windows CNG KSP or Azure Key Vault); this implementation provides the
///         plaintext encryption layer that those wrappers can be substituted above.</item>
/// </list>
///
/// <para><b>On-disk layout per <c>sessionRef</c>:</b></para>
/// <list type="bullet">
///   <item><c>&lt;sessionRef&gt;.key</c> — 32-byte raw AES-256 key (in production this file
///         should be key-wrapped or stored in a platform KSP, not stored plaintext).</item>
///   <item><c>&lt;sessionRef&gt;.blob</c> — 12-byte GCM nonce + 16-byte authentication tag +
///         ciphertext (variable length).</item>
/// </list>
/// </summary>
public sealed class SessionStore(string storageDirectory, ILogger<SessionStore> logger)
    : ISessionStore
{
    // AES-256-GCM parameters
    private const int KeyBytes  = 32;   // 256-bit key
    private const int NonceBytes = 12;  // 96-bit nonce (GCM standard)
    private const int TagBytes  = 16;   // 128-bit authentication tag

    // -------------------------------------------------------------------------
    // ISessionStore
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    /// <remarks>
    /// AC#2 / BRE-006: The raw storageState JSON is encrypted before persistence; only an
    /// opaque reference (random GUID) is returned to the caller.
    /// The plaintext buffer is zeroed from memory after encryption.
    /// </remarks>
    public async Task<string> SaveStorageStateAsync(
        string runId,
        string storageStateJson,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageStateJson);

        // Generate an opaque reference key — never derived from the secret value
        string sessionRef = Guid.NewGuid().ToString("N");

        byte[] key      = RandomNumberGenerator.GetBytes(KeyBytes);
        byte[] nonce    = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] tag      = new byte[TagBytes];
        byte[] plaintext = Encoding.UTF8.GetBytes(storageStateJson);
        byte[] ciphertext = new byte[plaintext.Length];

        try
        {
            using var aesGcm = new AesGcm(key, tagSizeInBytes: TagBytes);
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        finally
        {
            // Zero the plaintext buffer regardless of success or failure (AC#5)
            CryptographicOperations.ZeroMemory(plaintext);
        }

        // Assemble blob: [nonce (12)][tag (16)][ciphertext (variable)]
        byte[] blob = new byte[NonceBytes + TagBytes + ciphertext.Length];
        nonce.CopyTo(blob, 0);
        tag.CopyTo(blob, NonceBytes);
        ciphertext.CopyTo(blob, NonceBytes + TagBytes);

        Directory.CreateDirectory(storageDirectory);

        await File.WriteAllBytesAsync(KeyPath(sessionRef), key, ct).ConfigureAwait(false);
        await File.WriteAllBytesAsync(BlobPath(sessionRef), blob, ct).ConfigureAwait(false);

        // Zero the in-memory key after write — it is now only on disk
        CryptographicOperations.ZeroMemory(key);

        // AC#2 / AC#5: Never log the value — only the opaque ref and run association
        logger.LogInformation(
            "storageState persisted for run {RunId} → ref={SessionRef} (value not logged).",
            runId, sessionRef);

        return sessionRef;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// AC#2 / BRE-006: Decrypts the stored blob using AES-256-GCM.  Returns
    /// <see langword="null"/> when the reference does not exist or decryption fails —
    /// the caller must treat this as no approved profile (or as an expired reference, AC#6).
    /// The decrypted value is never logged.
    /// </remarks>
    public async Task<string?> LoadStorageStateAsync(string sessionRef, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionRef);

        string keyPath  = KeyPath(sessionRef);
        string blobPath = BlobPath(sessionRef);

        if (!File.Exists(keyPath) || !File.Exists(blobPath))
        {
            logger.LogDebug(
                "No stored storageState found for ref={SessionRef}.", sessionRef);
            return null;
        }

        byte[] key  = await File.ReadAllBytesAsync(keyPath,  ct).ConfigureAwait(false);
        byte[] blob = await File.ReadAllBytesAsync(blobPath, ct).ConfigureAwait(false);

        if (blob.Length < NonceBytes + TagBytes)
        {
            logger.LogWarning(
                "storageState blob for ref={SessionRef} is too short ({Length} bytes); " +
                "treating as corrupt/expired (BRE-006 / AC#6).",
                sessionRef, blob.Length);
            return null;
        }

        var nonce      = blob.AsSpan(0, NonceBytes);
        var tag        = blob.AsSpan(NonceBytes, TagBytes);
        var ciphertext = blob.AsSpan(NonceBytes + TagBytes);
        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aesGcm = new AesGcm(key, tagSizeInBytes: TagBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            // Decryption auth failure — treat as an invalid or tampered reference (AC#6)
            logger.LogWarning(
                "storageState decryption failed (auth tag mismatch) for ref={SessionRef}: {Msg}. " +
                "Treating as expired/invalid (BRE-006 / AC#6).",
                sessionRef, ex.Message);
            CryptographicOperations.ZeroMemory(plaintext);
            return null;
        }
        catch (CryptographicException ex)
        {
            logger.LogWarning(
                "storageState decryption failed for ref={SessionRef}: {Msg}. " +
                "Treating as expired/invalid (BRE-006 / AC#6).",
                sessionRef, ex.Message);
            CryptographicOperations.ZeroMemory(plaintext);
            return null;
        }
        finally
        {
            // Zero the in-memory key after use (AC#5)
            CryptographicOperations.ZeroMemory(key);
        }

        // AC#5: Decode to string — never log or trace this value
        string result = Encoding.UTF8.GetString(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);

        logger.LogDebug(
            "storageState loaded for ref={SessionRef} (value not logged).", sessionRef);

        return result;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string sessionRef, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionRef);

        int deleted = 0;

        foreach (string path in new[] { KeyPath(sessionRef), BlobPath(sessionRef) })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                deleted++;
            }
        }

        if (deleted > 0)
        {
            logger.LogInformation(
                "storageState blob deleted for ref={SessionRef} ({Count} file(s) removed).",
                sessionRef, deleted);
        }

        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private string KeyPath(string sessionRef)
        => Path.Combine(storageDirectory, $"{sessionRef}.key");

    private string BlobPath(string sessionRef)
        => Path.Combine(storageDirectory, $"{sessionRef}.blob");
}
