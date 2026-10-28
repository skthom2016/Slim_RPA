using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Actions;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Locators;

namespace TractoRuntime.Browser.FileTransfer;

/// <summary>
/// Implements <see cref="IFileDownloadHandler"/> for BRW-US-013 — File Download.
///
/// <para><b>AC#1 — Event correlation and validation</b><br/>
/// The handler uses Playwright's <c>RunAndWaitForDownloadAsync</c> to atomically race the
/// trigger action against the browser download event, ensuring the download is correlated
/// exactly to the click that caused it and not to an earlier or later download.
/// Filename and size are validated before the result is returned as <c>Succeeded=true</c>.
/// Content-type is validated by file-extension mapping when <see cref="DownloadRef.ExpectedContentType"/>
/// is specified; Playwright's <c>IDownload</c> does not expose the MIME type directly.</para>
///
/// <para><b>AC#2 — Unicode paths</b><br/>
/// <see cref="Directory.CreateDirectory"/> and <see cref="File.Exists"/> accept full Unicode
/// paths on .NET 10 / Windows; no special handling is required beyond a correct call.</para>
///
/// <para><b>AC#3 — Path allowlist before bytes are written</b><br/>
/// <see cref="IFileTransferPolicy.IsDownloadPathApproved"/> is called as the very first
/// step; if it returns <see langword="false"/> the method returns immediately without
/// initiating or saving any download.</para>
///
/// <para><b>AC#5 — Cancellation cleanup</b><br/>
/// If the <paramref name="ct"/> fires or any error occurs after Playwright has started
/// writing to the temporary download path and the file has been moved to
/// <see cref="DownloadRef.DestinationDirectory"/>, the handler deletes the local copy.</para>
/// </summary>
public sealed class FileDownloadHandler(
    IFileTransferPolicy policy,
    ISelectorStrategy selectorStrategy,
    ILogger<FileDownloadHandler> logger)
    : IFileDownloadHandler
{
    /// <inheritdoc/>
    public async Task<ActionResult> HandleAsync(
        IPage page,
        SelectorRef? triggerSelector,
        DownloadRef downloadRef,
        float timeoutMs,
        CancellationToken ct)
    {
        // AC#3: validate the destination directory BEFORE touching the browser or filesystem.
        if (!policy.IsDownloadPathApproved(downloadRef.DestinationDirectory))
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  =
                    $"Download destination '{downloadRef.DestinationDirectory}' is not in the " +
                    "approved download path allowlist (BRW-US-013 AC#3).",
                ErrorCategory = ErrorCategory.POLICY_DENIED,
                DetailCode    = "browser.downloadPathDenied"
            };
        }

        // Early cancellation check before any browser interaction.
        ct.ThrowIfCancellationRequested();

        // The path where the file was ultimately saved (used for cleanup on failure/cancel).
        string? savedFilePath = null;

        try
        {
            IDownload download;

            // AC#1: Use RunAndWaitForDownloadAsync so Playwright correlates the download event
            // to the trigger action atomically — no race between click and event subscription.
            if (triggerSelector is not null)
            {
                logger.LogInformation(
                    "Setting up download wait and resolving trigger locator.");

                var locator = await selectorStrategy
                    .ResolveAsync(page, triggerSelector, ct)
                    .ConfigureAwait(false);

                download = await page.RunAndWaitForDownloadAsync(
                    async () =>
                    {
                        // Click fires the browser-side download request; the download event
                        // is captured by the surrounding RunAndWaitForDownloadAsync wrapper.
                        await locator.ClickAsync(new LocatorClickOptions { Timeout = timeoutMs })
                            .ConfigureAwait(false);
                    },
                    new PageRunAndWaitForDownloadOptions { Timeout = timeoutMs }
                ).ConfigureAwait(false);
            }
            else
            {
                // No explicit trigger element — the download is already in-progress (e.g. a
                // direct-download navigation).  Wait for the next download event on this page.
                logger.LogInformation(
                    "Waiting for in-progress download event (no trigger selector specified).");

                download = await page.WaitForDownloadAsync(
                    new PageWaitForDownloadOptions { Timeout = timeoutMs }
                ).ConfigureAwait(false);
            }

            // ── Filename validation (AC#1) ──────────────────────────────────────────
            var suggestedFilename = download.SuggestedFilename;

            if (downloadRef.ExpectedFilename is { } expectedName &&
                !string.Equals(suggestedFilename, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    "Download filename mismatch: expected='{Expected}', received='{Got}'.",
                    expectedName, suggestedFilename);

                // Delete Playwright's internal temp copy before returning.
                await download.DeleteAsync().ConfigureAwait(false);

                return new ActionResult
                {
                    Succeeded     = false,
                    ErrorMessage  =
                        $"Downloaded filename '{suggestedFilename}' does not match " +
                        $"expected '{expectedName}'.",
                    ErrorCategory = ErrorCategory.VALIDATION,
                    DetailCode    = "browser.downloadFilenameMismatch"
                };
            }

            // ── Content-type validation via extension mapping (AC#1 best-effort) ────
            if (downloadRef.ExpectedContentType is { } expectedContentType)
            {
                var ext = Path.GetExtension(suggestedFilename);
                if (!ExtensionMatchesContentType(ext, expectedContentType))
                {
                    logger.LogWarning(
                        "Download content-type mismatch: extension='{Ext}', " +
                        "expectedContentType='{Expected}'.",
                        ext, expectedContentType);

                    await download.DeleteAsync().ConfigureAwait(false);

                    return new ActionResult
                    {
                        Succeeded     = false,
                        ErrorMessage  =
                            $"Downloaded file extension '{ext}' does not match " +
                            $"expected content-type '{expectedContentType}'.",
                        ErrorCategory = ErrorCategory.VALIDATION,
                        DetailCode    = "browser.downloadContentTypeMismatch"
                    };
                }
            }

            // ── Save to approved destination (AC#2: Unicode path support) ────────────
            // Path.GetFullPath accepts Unicode on .NET 10 / Windows without special treatment.
            var destDir  = Path.GetFullPath(downloadRef.DestinationDirectory);
            Directory.CreateDirectory(destDir);   // no-op if already exists

            savedFilePath = Path.Combine(destDir, suggestedFilename);

            logger.LogInformation(
                "Saving download '{Filename}' to '{Destination}'.",
                suggestedFilename, savedFilePath);

            await download.SaveAsAsync(savedFilePath).ConfigureAwait(false);

            // ── File-size validation (AC#1) after save ────────────────────────────────
            var fileInfo = new FileInfo(savedFilePath);

            if (downloadRef.MaxSizeBytes > 0 && fileInfo.Length > downloadRef.MaxSizeBytes)
            {
                logger.LogWarning(
                    "Downloaded file size {Size} bytes exceeds configured limit {Limit} bytes; " +
                    "removing file.",
                    fileInfo.Length, downloadRef.MaxSizeBytes);

                TryDeleteFile(savedFilePath);
                savedFilePath = null;

                return new ActionResult
                {
                    Succeeded     = false,
                    ErrorMessage  =
                        $"Downloaded file size {fileInfo.Length} bytes exceeds the " +
                        $"configured limit of {downloadRef.MaxSizeBytes} bytes.",
                    ErrorCategory = ErrorCategory.VALIDATION,
                    DetailCode    = "browser.downloadSizeExceeded"
                };
            }

            logger.LogInformation(
                "Download complete: file='{File}', sizeBytes={Size}.",
                savedFilePath, fileInfo.Length);

            return new ActionResult
            {
                Succeeded = true,
                Evidence  = new Dictionary<string, object?>
                {
                    ["downloadedFilePath"] = savedFilePath,
                    ["filename"]           = suggestedFilename,
                    ["fileSizeBytes"]      = fileInfo.Length
                }
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // AC#5: cancellation — remove any partial file written to the approved directory.
            if (savedFilePath is not null)
                TryDeleteFile(savedFilePath);

            logger.LogInformation(
                "Download cancelled; partial file cleaned up (BRW-US-013 AC#5).");

            // Re-throw so the caller can classify the run as CANCELLED.
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // AC#5: unexpected failure — remove any partial file.
            if (savedFilePath is not null)
                TryDeleteFile(savedFilePath);

            logger.LogError(ex,
                "Download step failed: {Message}", ex.Message);

            // Re-throw; the ActionExecutor catch-block classifies via IFailureClassifier.
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extension-to-MIME mapping for common types used in automation workflows.
    /// Playwright does not expose the server-sent Content-Type header through the
    /// IDownload API, so validation is based on the file extension from the
    /// Content-Disposition suggested filename.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExtensionMimeMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".csv"]  = ["text/csv", "text/plain", "application/csv"],
            [".json"] = ["application/json", "text/json"],
            [".xml"]  = ["application/xml", "text/xml"],
            [".pdf"]  = ["application/pdf"],
            [".xlsx"] = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"],
            [".xls"]  = ["application/vnd.ms-excel"],
            [".zip"]  = ["application/zip", "application/x-zip-compressed"],
            [".txt"]  = ["text/plain"],
            [".html"] = ["text/html"]
        };

    private static bool ExtensionMatchesContentType(string extension, string expectedContentType)
    {
        if (string.IsNullOrEmpty(extension))
            return false;

        if (!ExtensionMimeMap.TryGetValue(extension, out var knownTypes))
        {
            // Unknown extension — cannot confirm a match; deny rather than silently pass.
            return false;
        }

        return knownTypes.Any(t =>
            expectedContentType.StartsWith(t, StringComparison.OrdinalIgnoreCase));
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                logger.LogInformation("Deleted partial download file '{Path}'.", path);
            }
        }
        catch (Exception ex)
        {
            // Log and continue — cleanup failure must not suppress the original error.
            logger.LogWarning(ex,
                "Could not delete partial download file '{Path}'.", path);
        }
    }
}
