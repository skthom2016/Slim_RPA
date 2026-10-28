using Microsoft.Extensions.Logging;
using TractoRuntime.Browser.Binary;

namespace TractoRuntime.Browser.Packaging;

/// <summary>
/// Production implementation of <see cref="IOfflineBundleValidator"/> (BRW-US-021).
///
/// Verifies the following conditions before any run is permitted to start:
/// <list type="number">
///   <item>
///     The <see cref="OfflineBundleDescriptor.PlaywrightVersion"/> field is non-empty,
///     confirming that the bundle declares which Playwright for .NET version it ships
///     (AC#3 / BRW-AT-19).
///   </item>
///   <item>
///     Every <see cref="BrowserBinaryEntry"/> in the manifest has a corresponding binary
///     file present on disk at the resolved path (AC#1, AC#3 / BRW-AT-19).
///   </item>
/// </list>
///
/// The validator NEVER downloads, installs, or repairs missing browser binaries.
/// When a binary is absent, it throws <see cref="OfflineBundleException"/> and returns
/// immediately (AC#2 / BRW-AT-19, BRW-AT-28).
/// </summary>
public sealed class OfflineBundleValidator(ILogger<OfflineBundleValidator> logger) : IOfflineBundleValidator
{
    /// <inheritdoc />
    public Task ValidateAsync(OfflineBundleDescriptor descriptor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // ── AC#3: Playwright version reference must be present ─────────────────
        if (string.IsNullOrWhiteSpace(descriptor.PlaywrightVersion))
        {
            throw new OfflineBundleException(
                "The offline bundle descriptor does not declare a Playwright for .NET version. " +
                "The bundle is incomplete — every certified offline bundle must include " +
                "a pinned Playwright version reference (BRW-US-021 AC#3).");
        }

        // ── AC#1 / AC#3: All declared browser channels must have binaries on disk ─
        List<string> missingChannels = [];

        foreach (BrowserBinaryEntry entry in descriptor.BinaryManifest.Entries)
        {
            ct.ThrowIfCancellationRequested();

            string resolvedPath = ResolveBinaryPath(descriptor.BundleBaseDirectory, entry.RelativePath);

            if (File.Exists(resolvedPath))
            {
                logger.LogDebug(
                    "Offline bundle: binary present for channel '{Channel}' at '{Path}'.",
                    entry.Channel, resolvedPath);
            }
            else
            {
                // AC#2: Do NOT attempt to download or repair the binary.
                logger.LogError(
                    "Offline bundle: browser binary for channel '{Channel}' is missing at '{Path}'. " +
                    "No download will be attempted in isolated-host mode. " +
                    "Ensure the offline package includes all required browser binary files.",
                    entry.Channel, resolvedPath);

                missingChannels.Add(entry.Channel);
            }
        }

        if (missingChannels.Count > 0)
        {
            string channelList = string.Join(", ", missingChannels);
            throw new OfflineBundleException(
                $"Offline bundle is incomplete: browser binary files are missing for channel(s): " +
                $"{channelList}. " +
                "Ensure all required browser binary files are deployed as part of the offline package. " +
                "No internet download is permitted in isolated-host mode (BRW-US-021 AC#2).");
        }

        logger.LogInformation(
            "Offline bundle validated successfully. " +
            "PlaywrightVersion={PlaywrightVersion}, " +
            "Channels=[{Channels}], " +
            "BundleBaseDirectory={BundleBaseDirectory}",
            descriptor.PlaywrightVersion,
            string.Join(", ", descriptor.BinaryManifest.Entries.Select(e => e.Channel)),
            descriptor.BundleBaseDirectory);

        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves a binary path: if <paramref name="relativePath"/> is already absolute,
    /// returns it unchanged; otherwise combines it with <paramref name="baseDirectory"/>.
    /// </summary>
    private static string ResolveBinaryPath(string baseDirectory, string relativePath) =>
        Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(baseDirectory, relativePath);
}
