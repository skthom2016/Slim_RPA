using Microsoft.Playwright;
using TractoRuntime.Browser.Actions;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.FileTransfer;

/// <summary>
/// Handles <c>browser.file.download</c> steps: triggers a page action, awaits the
/// Playwright download event, validates filename / size, and saves the file to an approved
/// destination path (WEB-007 / BRW-US-013).
/// </summary>
/// <remarks>
/// The handler correlates the download event to the triggering action by using
/// Playwright's <c>RunAndWaitForDownloadAsync</c> pattern, which races the action against
/// the download event and fails if no download arrives within the configured timeout.
/// </remarks>
public interface IFileDownloadHandler
{
    /// <summary>
    /// Executes the download step and returns an <see cref="ActionResult"/>.
    /// </summary>
    /// <param name="page">Active Playwright page on which the download is triggered.</param>
    /// <param name="triggerSelector">
    ///   Optional locator for the element that initiates the download (e.g., an Export button).
    ///   When <see langword="null"/>, the download is expected to begin from an in-progress
    ///   navigation rather than an explicit click.
    /// </param>
    /// <param name="downloadRef">Validation parameters and approved destination directory.</param>
    /// <param name="timeoutMs">Per-step timeout in milliseconds (BRE-001).</param>
    /// <param name="ct">Cancellation token; on cancellation partial files are cleaned up (AC#5).</param>
    Task<ActionResult> HandleAsync(
        IPage page,
        SelectorRef? triggerSelector,
        DownloadRef downloadRef,
        float timeoutMs,
        CancellationToken ct);
}
