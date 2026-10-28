using Tracto.Desktop.Input;

namespace Tracto.Desktop.Security;

/// <summary>
/// Resolves a <see cref="SecretRef"/> to its decrypted plaintext in a <see cref="ClearableBuffer"/>.
/// The buffer MUST be disposed by the caller after use.
/// </summary>
public interface ISecretResolver
{
    /// <summary>
    /// Resolves the secret identified by <paramref name="secretRef"/> and returns its plaintext
    /// in a <see cref="ClearableBuffer"/> that the caller is responsible for disposing.
    /// </summary>
    /// <param name="secretRef">The secret reference to resolve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ClearableBuffer"/> containing the plaintext characters.</returns>
    Task<ClearableBuffer> ResolveAsync(SecretRef secretRef, CancellationToken ct);
}
