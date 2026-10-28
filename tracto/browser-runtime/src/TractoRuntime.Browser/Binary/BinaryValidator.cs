using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TractoRuntime.Browser.Packaging;

namespace TractoRuntime.Browser.Binary;

/// <summary>
/// CEC-compliant implementation of <see cref="IBinaryValidator"/> (BRW-001 / BRW-US-001).
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Verify the Playwright browser executable exists at <paramref name="binaryPath"/>.</item>
///   <item>
///     Compute the SHA-256 digest of the binary and compare it against the pinned value in
///     <see cref="BrowserBinaryManifest"/>.
///   </item>
///   <item>
///     In isolated-host mode (<see cref="IRuntimePackage.IsIsolatedDeploymentAsync"/> returns
///     <c>true</c>) reject any attempt to download missing binaries — the bundle must be
///     self-contained (BRW-AT-19).
///   </item>
///   <item>
///     Expose the validated browser version string through <see cref="LastValidatedVersion"/>
///     for inclusion in the capability digest (BRW-006).
///   </item>
/// </list>
///
/// On failure the validator throws <see cref="BinaryValidationException"/> with
/// <c>errorCategory=ENVIRONMENT</c> and <c>detailCode=browser.notInstalled</c>,
/// which maps directly to the CEC v1.0.1 error wire record.
/// </summary>
public sealed class BinaryValidator(
    BrowserBinaryManifest manifest,
    IRuntimePackage runtimePackage,
    ILogger<BinaryValidator> logger) : IBinaryValidator
{
    // Exposed for capability-digest assembly (BRW-006).
    public string? LastValidatedVersion { get; private set; }

    /// <inheritdoc />
    public async Task ValidateAsync(string binaryPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);

        bool isIsolated = await runtimePackage.IsIsolatedDeploymentAsync(ct).ConfigureAwait(false);

        // --- 1. Existence check --------------------------------------------------
        if (!File.Exists(binaryPath))
        {
            if (isIsolated)
            {
                // Isolated-host rule: no download is ever attempted (BRW-AT-19).
                logger.LogError(
                    "Browser binary not found at {BinaryPath} in isolated-host mode; " +
                    "download is prohibited. Ensure the offline bundle includes the binary.",
                    binaryPath);
            }
            else
            {
                logger.LogError(
                    "Browser binary not found at {BinaryPath}. " +
                    "Install the required Playwright browser binaries before starting the runtime.",
                    binaryPath);
            }

            throw new BinaryValidationException(
                $"Browser binary not found at '{binaryPath}'.",
                "browser.notInstalled");
        }

        // --- 2. Resolve pinned manifest entry ------------------------------------
        string channel = ResolveChannelFromPath(binaryPath);
        BrowserBinaryEntry? entry = manifest.FindEntry(channel)
            ?? manifest.FindEntry(manifest.DefaultChannel);

        if (entry is null)
        {
            throw new BinaryValidationException(
                $"No pinned manifest entry found for channel '{channel}'. " +
                "The browser binary cannot be trusted.",
                "browser.notInstalled");
        }

        // --- 3. Hash verification ------------------------------------------------
        string actualHash = await ComputeSha256HexAsync(binaryPath, ct).ConfigureAwait(false);

        if (!string.Equals(actualHash, entry.Sha256Hex, StringComparison.OrdinalIgnoreCase))
        {
            // Do NOT log the actual or expected hashes at ERROR level to avoid leaking
            // integrity details that could aid tampering; log them at DEBUG only.
            logger.LogDebug(
                "Binary hash mismatch for {Channel}. Expected={Expected}, Actual={Actual}",
                channel, entry.Sha256Hex, actualHash);

            logger.LogError(
                "Browser binary at '{BinaryPath}' does not match the pinned SHA-256 digest " +
                "for channel '{Channel}'. The binary is rejected.",
                binaryPath, channel);

            throw new BinaryValidationException(
                $"Browser binary signature mismatch for channel '{channel}'. " +
                "The binary does not match the approved version list.",
                "browser.notInstalled");
        }

        // --- 4. Record validated version for capability digest (BRW-006) ---------
        LastValidatedVersion = entry.DisplayVersion ?? entry.Revision;

        logger.LogInformation(
            "Browser binary validated. Channel={Channel}, Version={Version}, Path={BinaryPath}",
            channel, LastValidatedVersion, binaryPath);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Derives the browser channel name from the executable path heuristic.
    /// Falls back to the manifest's <see cref="BrowserBinaryManifest.DefaultChannel"/>.
    /// </summary>
    private string ResolveChannelFromPath(string binaryPath)
    {
        string lower = binaryPath.ToLowerInvariant();

        return lower switch
        {
            _ when lower.Contains("firefox")  => "firefox",
            _ when lower.Contains("webkit")   => "webkit",
            _ when lower.Contains("chromium") => "chromium",
            _ when lower.Contains("chrome")   => "chromium",
            _                                 => manifest.DefaultChannel
        };
    }

    /// <summary>
    /// Computes the lowercase hex-encoded SHA-256 digest of the file at <paramref name="path"/>
    /// using async streaming to avoid blocking the thread pool on large binaries.
    /// </summary>
    private static async Task<string> ComputeSha256HexAsync(string path, CancellationToken ct)
    {
        await using FileStream fs = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            useAsync: true);

        byte[] hashBytes = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
