using Tracto.Desktop.Element;

namespace Tracto.Desktop.Controls;

/// <summary>
/// Production implementation of <see cref="IConfirmDialogHandler"/>.
/// Clicks an allowed button in a confirm dialog; unapproved dismiss actions raise
/// <see cref="DesktopActionError"/> with code <c>"POLICY_DENIED"</c>.
/// </summary>
public sealed class ConfirmDialogHandler : IConfirmDialogHandler
{
    private readonly IUiaPatternProvider _patterns;

    /// <summary>
    /// Buttons whose names are unconditionally permitted as confirm actions.
    /// </summary>
    private static readonly HashSet<string> AllowedConfirmButtons = new(StringComparer.OrdinalIgnoreCase)
    {
        "Save", "OK", "Yes", "Open", "Continue", "Accept"
    };

    /// <summary>
    /// Buttons whose names are unconditionally permitted as deny/dismiss actions.
    /// Any button not in this list — or any undeclared dismiss — raises POLICY_DENIED.
    /// </summary>
    private static readonly HashSet<string> AllowedDenyButtons = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cancel", "No", "Discard", "Close", "Dismiss"
    };

    public ConfirmDialogHandler(IUiaPatternProvider patterns)
    {
        _patterns = patterns ?? throw new ArgumentNullException(nameof(patterns));
    }

    /// <inheritdoc/>
    public Task ConfirmAsync(
        ElementRef        dialogEl,
        string            buttonName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dialogEl);
        ArgumentNullException.ThrowIfNull(buttonName);

        ElementRef button = RequireButton(dialogEl, buttonName);
        _patterns.InvokeElement(button);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DenyAsync(
        ElementRef        dialogEl,
        string            buttonName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dialogEl);
        ArgumentNullException.ThrowIfNull(buttonName);

        // Policy guard: unapproved dismiss raises POLICY_DENIED before any UI interaction.
        if (!AllowedDenyButtons.Contains(buttonName))
            throw new DesktopActionError("POLICY_DENIED",
                $"desktop.dialogDismissNotAllowed: '{buttonName}' is not an approved deny action");

        ElementRef button = RequireButton(dialogEl, buttonName);
        _patterns.InvokeElement(button);
        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ElementRef RequireButton(ElementRef dialogEl, string buttonName)
    {
        ElementRef? button = _patterns.FindButtonByName(dialogEl, buttonName);
        if (button is null)
            throw new DesktopActionError("TARGET_NOT_FOUND",
                $"desktop.dialogButtonNotFound: '{buttonName}'");
        return button;
    }
}
