using Microsoft.Playwright;
using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Dialogs;

/// <summary>
/// Manages JavaScript and browser-native dialog interactions (BRW-US-012, WEB-005).
/// <list type="bullet">
///   <item>AC#3 — Responds to alert/confirm/prompt with a declared choice and verifies closure.</item>
///   <item>AC#4 — Detects unhandled browser-native permission prompts and reports them as errors
///               without guessing an answer.</item>
/// </list>
/// </summary>
public interface IDialogHandler
{
    /// <summary>
    /// Installs a persistent dialog listener on <paramref name="page"/>.
    /// Dialogs that arrive without a pre-armed declaration are reported as errors (AC#4).
    /// Must be called once per page — typically immediately after the page is created or navigated.
    /// </summary>
    void Install(IPage page);

    /// <summary>
    /// Arms a one-shot response for the NEXT dialog that appears on the current page.
    /// Must be called before the step that triggers the dialog (AC#3).
    /// </summary>
    void ArmResponse(DialogDeclaration declaration);

    /// <summary>
    /// Returns and clears any unhandled-dialog error that occurred since the last call.
    /// Returns <see langword="null"/> when no error is pending.
    /// </summary>
    CecError? ConsumeUnhandledDialogError();

    /// <summary>Returns <see langword="true"/> if an unhandled dialog error is pending.</summary>
    bool HasUnhandledDialogError { get; }
}
