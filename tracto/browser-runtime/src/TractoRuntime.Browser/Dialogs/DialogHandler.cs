using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Dialogs;

/// <summary>
/// Implements <see cref="IDialogHandler"/> for BRW-US-012 (WEB-005) — Dialogs and Permission Prompts.
///
/// <para><b>AC#3 — JavaScript alert / confirm / prompt:</b><br/>
/// The caller arms a response via <see cref="ArmResponse"/> before the step that triggers the
/// dialog.  When the dialog fires, <see cref="OnDialog"/> dequeues the armed declaration,
/// validates the message (if <c>ExpectedMessageContains</c> is set), and responds with the
/// declared choice (accept/dismiss/fill text for prompt).  The dialog closes, and the evidence
/// record notes the dialog type and response.</para>
///
/// <para><b>AC#4 — Unhandled permission prompts:</b><br/>
/// Any dialog that arrives without a pre-armed declaration (unexpected JS dialog, browser-native
/// permission prompt) is recorded as an error.  The runtime does NOT guess a response; it
/// does NOT silently dismiss.  The error is surfaced via <see cref="ConsumeUnhandledDialogError"/>
/// and must be treated as a run-level failure
/// (errorCategory=POLICY_DENIED, detailCode=browser.unexpectedDialog).</para>
///
/// <para><b>Security note:</b><br/>
/// <see cref="DialogDeclaration.PromptText"/> must never contain a raw secret.  The prompt
/// text is written to log output (at Debug level) and to evidence — only non-sensitive values
/// are permitted here.</para>
/// </summary>
public sealed class DialogHandler(ILogger<DialogHandler> logger) : IDialogHandler
{
    // One-shot armed response.  Only one dialog can be armed at a time; a second call to
    // ArmResponse before the first dialog fires replaces the prior declaration (last wins).
    private DialogDeclaration? _armedDeclaration;
    private readonly object _armLock = new();

    // Pending unhandled-dialog error (AC#4).
    private CecError? _unhandledError;
    private readonly object _errorLock = new();

    private bool _installed;

    // ─────────────────────────────────────────────────────────────────────────
    // IDialogHandler — installation
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void Install(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (_installed)
        {
            logger.LogWarning(
                "DialogHandler.Install called more than once on the same handler instance. " +
                "Skipping duplicate installation.");
            return;
        }

        page.Dialog += OnDialog;
        _installed = true;

        logger.LogDebug(
            "DialogHandler installed on page '{Url}' (BRW-US-012 WEB-005).", page.Url);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IDialogHandler — arming
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void ArmResponse(DialogDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        lock (_armLock)
        {
            _armedDeclaration = declaration;
        }

        logger.LogDebug(
            "Dialog response armed: choice={Choice}, expectedMsg={ExpectedMsg}.",
            declaration.Accept ? "accept" : "dismiss",
            declaration.ExpectedMessageContains ?? "(any)");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IDialogHandler — error polling
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool HasUnhandledDialogError
    {
        get { lock (_errorLock) { return _unhandledError is not null; } }
    }

    /// <inheritdoc/>
    public CecError? ConsumeUnhandledDialogError()
    {
        lock (_errorLock)
        {
            var err = _unhandledError;
            _unhandledError = null;
            return err;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Event handler — executes on Playwright's event-dispatch thread
    // ─────────────────────────────────────────────────────────────────────────

    private void OnDialog(object? sender, IDialog dialog)
    {
        DialogDeclaration? declaration;
        lock (_armLock)
        {
            declaration = _armedDeclaration;
            _armedDeclaration = null;  // consume the one-shot
        }

        if (declaration is null)
        {
            // AC#4 — no armed response; do NOT guess.
            // Playwright requires the dialog to be handled or it will block navigation;
            // we dismiss it (the safest no-op for native prompts) and record the error.
            // The error is surfaced to the step runner via ConsumeUnhandledDialogError.
            _ = dialog.DismissAsync();

            var message = dialog.Type.Equals("alert", StringComparison.OrdinalIgnoreCase)
                ? $"An unhandled dialog of type '{dialog.Type}' appeared with message: \"{dialog.Message}\". " +
                  "Declare the expected dialog response with browser.dialog.arm before the triggering action."
                : $"An undeclared browser-native dialog or permission prompt of type '{dialog.Type}' appeared. " +
                  "The runtime does not guess a response (BRW-US-012 AC#4).";

            var err = new CecError
            {
                Category   = ErrorCategory.POLICY_DENIED,
                Message    = message,
                DetailCode = "browser.unexpectedDialog"
            };

            lock (_errorLock)
            {
                _unhandledError = err;
            }

            logger.LogWarning(
                "Unhandled dialog detected: type={Type}, message=\"{Msg}\". " +
                "Run must be stopped (BRW-US-012 AC#4).",
                dialog.Type, dialog.Message);
            return;
        }

        // AC#3 — validate message if the declaration specifies an expectation.
        if (declaration.ExpectedMessageContains is { } expected &&
            !dialog.Message.Contains(expected, StringComparison.OrdinalIgnoreCase))
        {
            _ = dialog.DismissAsync();

            var mismatchErr = new CecError
            {
                Category   = ErrorCategory.VALIDATION,
                Message    = $"Dialog message \"{dialog.Message}\" does not contain expected substring \"{expected}\".",
                DetailCode = "browser.dialogMessageMismatch"
            };

            lock (_errorLock)
            {
                _unhandledError = mismatchErr;
            }

            logger.LogWarning(
                "Dialog message mismatch: expected substring '{Expected}', got '{Actual}'.",
                expected, dialog.Message);
            return;
        }

        // AC#3 — respond with the declared choice.
        if (declaration.Accept)
        {
            var promptText = declaration.PromptText;
            _ = promptText is not null
                ? dialog.AcceptAsync(promptText)
                : dialog.AcceptAsync();

            logger.LogInformation(
                "Dialog accepted: type={Type}, promptText={HasText}.",
                dialog.Type, promptText is not null);
        }
        else
        {
            _ = dialog.DismissAsync();

            logger.LogInformation("Dialog dismissed: type={Type}.", dialog.Type);
        }
    }
}
