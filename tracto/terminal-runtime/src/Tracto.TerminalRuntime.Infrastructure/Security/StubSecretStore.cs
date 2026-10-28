using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Interfaces;

namespace Tracto.TerminalRuntime.Infrastructure.Security;

/// <summary>
/// A stub <see cref="ISecretStore"/> for local development and testing.
/// Returns a deterministically opaque value derived from the secret reference so
/// that the resolved secret is never a plain-text copy of the reference itself,
/// but also never requires a live secret store connection.
///
/// IMPORTANT: This implementation is for non-production use only.
/// Replace with a real vault-backed implementation before shipping.
/// </summary>
public sealed class StubSecretStore : ISecretStore
{
    /// <inheritdoc/>
    public Task<Result<string, Error>> GetSecretAsync(
        string secretRef,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretRef);

        // Return a non-empty, opaque token. The value is intentionally NOT the
        // secretRef itself so that accidental logging never leaks the key name
        // as a value. In a real implementation this would call a vault API.
        var opaqueValue = $"stub:{ComputeOpaqueToken(secretRef)}";
        return Task.FromResult(Result<string, Error>.Ok(opaqueValue));
    }

    /// <summary>
    /// Produces a short, stable, opaque token for a secret reference using FNV-1a hashing.
    /// This is NOT cryptographic — it only makes the returned value differ from the input.
    /// </summary>
    private static string ComputeOpaqueToken(string secretRef)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(secretRef);
        uint acc = 0x811C_9DC5u; // FNV-1a offset basis
        foreach (var b in bytes)
            acc = (acc ^ b) * 0x0100_0193u; // FNV-1a prime
        return acc.ToString("x8");
    }
}
