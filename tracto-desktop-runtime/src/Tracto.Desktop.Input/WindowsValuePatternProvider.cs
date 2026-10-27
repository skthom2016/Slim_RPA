using System.Diagnostics;
using System.Windows.Automation;
using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// Production implementation of <see cref="IValuePatternProvider"/> using Windows UI Automation.
/// </summary>
public sealed class WindowsValuePatternProvider : IValuePatternProvider
{
    /// <inheritdoc/>
    public bool HasValuePattern(ElementRef target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.AutomationElement.GetSupportedPatterns()
            .Any(p => p.Id == ValuePattern.Pattern.Id);
    }

    /// <inheritdoc/>
    public void SetValue(ElementRef target, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        var pattern = (ValuePattern)target.AutomationElement.GetCurrentPattern(ValuePattern.Pattern);
        pattern.SetValue(value);
    }

    /// <inheritdoc/>
    public bool EnsureKeyboardFocus(ElementRef target, int timeoutMs = 2000)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.AutomationElement.Current.HasKeyboardFocus)
            return true;

        try
        {
            target.AutomationElement.SetFocus();
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (target.AutomationElement.Current.HasKeyboardFocus)
                return true;
            Thread.Sleep(50);
        }
        return false;
    }
}
