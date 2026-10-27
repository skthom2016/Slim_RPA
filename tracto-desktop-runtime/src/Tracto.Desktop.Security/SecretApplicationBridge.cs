using Tracto.Desktop.Element;
using Tracto.Desktop.Input;

namespace Tracto.Desktop.Security;

/// <summary>
/// Bridges secret resolution to keyboard delivery.
/// The resolved plaintext is typed into the target element and NEVER
/// returned to callers or materialised beyond the scope of this method.
/// </summary>
public sealed class SecretApplicationBridge
{
    /// <summary>
    /// Resolves <paramref name="secret"/>, types the plaintext into <paramref name="target"/>,
    /// and disposes the buffer regardless of outcome.
    /// The resolved secret value is NEVER returned to callers.
    /// </summary>
    /// <param name="secret">The secret reference to resolve.</param>
    /// <param name="target">The UI element that receives the typed input.</param>
    /// <param name="kb">The keyboard engine used to deliver the input.</param>
    /// <param name="resolver">The resolver that decrypts the secret.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task ApplySecretAsync(
        SecretRef secret,
        ElementRef target,
        IKeyboardInputEngine kb,
        ISecretResolver resolver,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(kb);
        ArgumentNullException.ThrowIfNull(resolver);

        ClearableBuffer? buffer = null;
        try
        {
            buffer = await resolver.ResolveAsync(secret, ct).ConfigureAwait(false);

            // Convert span to char[] for string construction.
            // The temporary string exists only within this method scope and is never returned.
            var chars = buffer.Span.ToArray();
            var text = new string(chars);
            Array.Clear(chars, 0, chars.Length);

            var options = new TypeOptions(target, text);
            await kb.TypeAsync(options, ct).ConfigureAwait(false);
        }
        finally
        {
            // Dispose clears all bytes in the buffer regardless of outcome.
            buffer?.Dispose();
        }
    }
}
