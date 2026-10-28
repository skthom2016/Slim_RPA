using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Abstraction over UIA ValuePattern so the keyboard engine can be tested
/// without a live Windows Automation session.
/// </summary>
public interface IValuePatternProvider
{
    /// <summary>Returns true when the target element supports ValuePattern.</summary>
    bool HasValuePattern(ElementRef target);

    /// <summary>
    /// Calls ValuePattern.SetValue on the target element.
    /// Callers must verify <see cref="HasValuePattern"/> before calling this.
    /// </summary>
    void SetValue(ElementRef target, string value);

    /// <summary>
    /// Ensures the element has keyboard focus, calling SetFocus() if needed.
    /// Returns false when the focus cannot be obtained within <paramref name="timeoutMs"/>.
    /// </summary>
    bool EnsureKeyboardFocus(ElementRef target, int timeoutMs = 2000);
}
