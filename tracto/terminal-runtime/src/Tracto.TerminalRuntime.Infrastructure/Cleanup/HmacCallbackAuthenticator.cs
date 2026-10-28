using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Tracto.TerminalRuntime.Application.Cleanup;
using Tracto.TerminalRuntime.Application.Common;

namespace Tracto.TerminalRuntime.Infrastructure.Cleanup;

/// <summary>
/// HMAC-SHA256 implementation of <see cref="ICallbackAuthenticator"/>.
///
/// The configured <c>sharedSecret</c> is used as the HMAC key.  Verification is
/// performed with <see cref="CryptographicOperations.FixedTimeEquals"/> to prevent
/// timing-based attacks.
///
/// CEC invariant #4: the raw HMAC value is never written to logs or persisted.
/// Only a boolean pass/fail result is emitted at debug level.
/// </summary>
public sealed class HmacCallbackAuthenticator(
    string sharedSecret,
    ILogger<HmacCallbackAuthenticator> logger) : ICallbackAuthenticator
{
    private readonly byte[] _keyBytes = Encoding.UTF8.GetBytes(sharedSecret);

    /// <inheritdoc />
    public Task<Result<bool, Error>> AuthenticateAsync(
        string callbackId,
        string callbackHmac,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callbackHmac))
        {
            logger.LogDebug("Callback authentication failed: no HMAC provided.");
            return Task.FromResult(
                Result<bool, Error>.Fail(Error.Authentication(
                    "Callback.Unauthenticated",
                    "The inbound callback did not supply a credential.")));
        }

        // Compute expected HMAC over the callbackId (delivery-specific, not reusable).
        byte[] expectedBytes;
        try
        {
            expectedBytes = HMACSHA256.HashData(_keyBytes, Encoding.UTF8.GetBytes(callbackId));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to compute expected HMAC for callback authentication.");
            return Task.FromResult(
                Result<bool, Error>.Fail(Error.General(
                    "Callback.AuthComputeFailure",
                    "Internal error computing callback credential.")));
        }

        byte[] suppliedBytes;
        try
        {
            suppliedBytes = Convert.FromHexString(callbackHmac);
        }
        catch (FormatException)
        {
            // Raw token not logged — only a category message (CEC #4).
            logger.LogDebug("Callback authentication failed: HMAC is not valid hex.");
            return Task.FromResult(
                Result<bool, Error>.Fail(Error.Authentication(
                    "Callback.Unauthenticated",
                    "The inbound callback credential is not in the expected format.")));
        }

        // Constant-time comparison prevents timing oracle attacks.
        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
        {
            // Do NOT log the supplied value (CEC invariant #4).
            logger.LogDebug("Callback authentication failed: HMAC mismatch.");
            return Task.FromResult(
                Result<bool, Error>.Fail(Error.Authentication(
                    "Callback.Unauthenticated",
                    "The inbound callback credential did not match.")));
        }

        return Task.FromResult(Result<bool, Error>.Ok(true));
    }
}
