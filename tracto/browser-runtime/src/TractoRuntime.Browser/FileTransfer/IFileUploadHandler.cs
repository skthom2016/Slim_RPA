using Microsoft.Playwright;
using TractoRuntime.Browser.Actions;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.FileTransfer;

/// <summary>
/// Handles <c>browser.file.upload</c> steps using Playwright's <c>setInputFiles</c>
/// or file-chooser API with approved-path policy enforcement (WEB-006 / BRW-US-013).
/// </summary>
public interface IFileUploadHandler
{
    /// <summary>
    /// Executes the upload step and returns an <see cref="ActionResult"/>.
    /// </summary>
    /// <param name="page">Active Playwright page on which the upload is performed.</param>
    /// <param name="targetSelector">
    ///   Locator for the upload element — either an <c>&lt;input type="file"&gt;</c> element
    ///   (direct <c>setInputFiles</c>) or a custom upload trigger (file-chooser approach).
    /// </param>
    /// <param name="uploadRef">Upload parameters including the approved source path.</param>
    /// <param name="timeoutMs">Per-step timeout in milliseconds (BRE-001).</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ActionResult> HandleAsync(
        IPage page,
        SelectorRef targetSelector,
        UploadRef uploadRef,
        float timeoutMs,
        CancellationToken ct);
}
