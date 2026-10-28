namespace TractoRuntime.Browser.Dialogs;

/// <summary>
/// Specifies how the runtime should respond to an expected JavaScript dialog
/// (alert / confirm / prompt) — BRW-US-012 WEB-005 / AC#3.
/// </summary>
public sealed record DialogDeclaration
{
    /// <summary>
    /// Whether to accept (<c>true</c>) or dismiss (<c>false</c>) the dialog.
    /// For <c>alert</c> dialogs, both choices close the dialog; <c>false</c> (dismiss) is
    /// functionally equivalent to clicking "OK" on an alert.
    /// </summary>
    public required bool Accept { get; init; }

    /// <summary>
    /// Text to supply for a <c>prompt</c> dialog.  Only meaningful when <see cref="Accept"/> is
    /// <see langword="true"/> and the dialog type is <c>prompt</c>; ignored otherwise.
    /// Must never contain a raw secret or credential value; use a secret reference injected
    /// by the runtime if the prompt requires a password.
    /// </summary>
    public string? PromptText { get; init; }

    /// <summary>
    /// Optional assertion: the dialog's <c>message</c> text must contain this substring.
    /// When set, the handler verifies the message before responding; a mismatch is reported
    /// as <c>errorCategory=VALIDATION, detailCode=browser.dialogMessageMismatch</c>.
    /// </summary>
    public string? ExpectedMessageContains { get; init; }
}
