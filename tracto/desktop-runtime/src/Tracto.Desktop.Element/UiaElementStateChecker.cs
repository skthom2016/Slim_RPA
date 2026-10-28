using System.Windows.Automation;

namespace Tracto.Desktop.Element;

/// <summary>
/// Live UIA implementation of <see cref="IElementStateChecker"/>.
/// Reads element state via the UIAutomation COM layer.
/// </summary>
public sealed class UiaElementStateChecker : IElementStateChecker
{
    /// <inheritdoc />
    public Task<ElementStateResult> CheckAsync(ElementRef el, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var ae = el.AutomationElement;

        try
        {
            var current = ae.Current;

            bool isEnabled = current.IsEnabled;
            bool isVisible = !current.IsOffscreen;

            // HasProvider: element supports at least one UIA pattern.
            bool hasProvider = ae.GetSupportedPatterns().Length > 0;

            return Task.FromResult(new ElementStateResult
            {
                IsEnabled = isEnabled,
                IsVisible = isVisible,
                HasProvider = hasProvider,
                AccessDeniedReason = null,
            });
        }
        catch (ElementNotAvailableException ex)
        {
            // Elevated-integrity processes deny UIA access via ElementNotAvailableException.
            // Distinguish access-denied from ordinary staleness by checking the HResult.
            // E_ACCESSDENIED is 0x80070005; UIA wraps it as ElementNotAvailableException.
            // We treat all such exceptions conservatively as policy-denied when the element
            // is reported as still alive in the process list but inaccessible.
            if (IsAccessDenied(ex))
            {
                return Task.FromResult(new ElementStateResult
                {
                    IsEnabled = false,
                    IsVisible = false,
                    HasProvider = false,
                    AccessDeniedReason = "POLICY_DENIED",
                });
            }

            throw new DesktopActionError("TARGET_NOT_FOUND", "desktop.elementStale", ex);
        }
    }

    private static bool IsAccessDenied(ElementNotAvailableException ex)
    {
        // HResult 0x80070005 = E_ACCESSDENIED (elevated process boundary).
        const int E_ACCESSDENIED = unchecked((int)0x80070005);
        return ex.HResult == E_ACCESSDENIED;
    }
}
