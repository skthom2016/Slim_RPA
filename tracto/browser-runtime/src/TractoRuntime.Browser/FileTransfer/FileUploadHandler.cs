using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Actions;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Locators;

namespace TractoRuntime.Browser.FileTransfer;

/// <summary>
/// Implements <see cref="IFileUploadHandler"/> for BRW-US-013 — File Upload.
///
/// <para><b>AC#4 — Approved path check before transfer</b><br/>
/// <see cref="IFileTransferPolicy.IsUploadPathApproved"/> is called as the very first
/// step; if it returns <see langword="false"/> the method returns immediately without
/// initiating any browser interaction or file transfer.</para>
///
/// <para><b>Upload strategy (WEB-006)</b><br/>
/// The handler first attempts Playwright's <c>SetInputFilesAsync</c> directly on the
/// resolved locator (handles <c>&lt;input type="file"&gt;</c> elements).  When that
/// call raises a Playwright exception indicating the element is not a file input, the
/// handler falls back to the file-chooser approach:
/// <c>page.RunAndWaitForFileChooserAsync</c> → click → <c>fileChooser.SetFilesAsync</c>.
/// This covers custom upload buttons that open the OS file-picker dialog.</para>
/// </summary>
public sealed class FileUploadHandler(
    IFileTransferPolicy policy,
    ISelectorStrategy selectorStrategy,
    ILogger<FileUploadHandler> logger)
    : IFileUploadHandler
{
    /// <inheritdoc/>
    public async Task<ActionResult> HandleAsync(
        IPage page,
        SelectorRef targetSelector,
        UploadRef uploadRef,
        float timeoutMs,
        CancellationToken ct)
    {
        // AC#4: validate the source path BEFORE any browser or file system interaction.
        if (!policy.IsUploadPathApproved(uploadRef.SourcePath))
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  =
                    $"Upload source path '{uploadRef.SourcePath}' is not in the " +
                    "approved upload path allowlist (BRW-US-013 AC#4).",
                ErrorCategory = ErrorCategory.POLICY_DENIED,
                DetailCode    = "browser.uploadPathDenied"
            };
        }

        // Verify the source file actually exists before sending any bytes to the browser.
        if (!File.Exists(uploadRef.SourcePath))
        {
            return new ActionResult
            {
                Succeeded     = false,
                ErrorMessage  = $"Upload source file '{uploadRef.SourcePath}' does not exist.",
                ErrorCategory = ErrorCategory.ENVIRONMENT,
                DetailCode    = "browser.uploadFileNotFound"
            };
        }

        // Early cancellation check before touching the browser.
        ct.ThrowIfCancellationRequested();

        // Resolve the locator fresh — DOM-006: always re-resolve to handle re-renders.
        var locator = await selectorStrategy
            .ResolveAsync(page, targetSelector, ct)
            .ConfigureAwait(false);

        logger.LogInformation(
            "Attempting direct SetInputFiles for upload source '{Source}'.",
            uploadRef.SourcePath);

        try
        {
            // Primary approach: SetInputFilesAsync works directly on <input type="file"> elements.
            await locator.SetInputFilesAsync(
                uploadRef.SourcePath,
                new LocatorSetInputFilesOptions { Timeout = timeoutMs }
            ).ConfigureAwait(false);

            logger.LogInformation("Upload via SetInputFiles succeeded.");

            return new ActionResult
            {
                Succeeded = true,
                Evidence  = new Dictionary<string, object?>
                {
                    ["uploadedFile"]     = uploadRef.SourcePath,
                    ["uploadStrategy"]   = "setInputFiles"
                }
            };
        }
        catch (PlaywrightException pwEx) when (IsNotFileInputError(pwEx.Message))
        {
            // The element is not a plain <input type="file"> — fall back to the file-chooser
            // approach which handles custom upload buttons that open the OS file-picker.
            logger.LogInformation(
                "SetInputFiles rejected (not a file-input element); " +
                "falling back to file-chooser approach.");
        }

        // Fallback approach: RunAndWaitForFileChooserAsync + click + SetFilesAsync.
        // This works for custom buttons that trigger the OS file-picker dialog.
        logger.LogInformation(
            "Setting up file-chooser wait before clicking upload trigger.");

        var fileChooser = await page.RunAndWaitForFileChooserAsync(
            async () =>
            {
                // Re-resolve after the earlier attempt to ensure a fresh locator.
                var freshLocator = await selectorStrategy
                    .ResolveAsync(page, targetSelector, ct)
                    .ConfigureAwait(false);

                await freshLocator
                    .ClickAsync(new LocatorClickOptions { Timeout = timeoutMs })
                    .ConfigureAwait(false);
            },
            new PageRunAndWaitForFileChooserOptions { Timeout = timeoutMs }
        ).ConfigureAwait(false);

        await fileChooser
            .SetFilesAsync(uploadRef.SourcePath)
            .ConfigureAwait(false);

        logger.LogInformation("Upload via file-chooser succeeded.");

        return new ActionResult
        {
            Succeeded = true,
            Evidence  = new Dictionary<string, object?>
            {
                ["uploadedFile"]   = uploadRef.SourcePath,
                ["uploadStrategy"] = "fileChooser"
            }
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Detects Playwright error messages that indicate <c>SetInputFiles</c> was called on
    /// an element that is not a file input.  These tokens come from Playwright's internal
    /// validation messages and are used only to choose the fallback strategy, not to alter
    /// the CEC error classification.
    /// </summary>
    private static readonly string[] NotFileInputTokens =
    [
        "is not a file input",
        "input element of type file",
        "not an input element"
    ];

    private static bool IsNotFileInputError(string message) =>
        NotFileInputTokens.Any(t =>
            message.Contains(t, StringComparison.OrdinalIgnoreCase));
}
