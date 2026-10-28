namespace Tracto.Designer.Infrastructure.Desktop;

using System.Windows.Automation;

internal static class UiaCapture
{
    public record ElementInfo(string? Name, string? AutomationId, string? ClassName, string? ControlTypeName, bool IsPassword, string? Value);

    /// <summary>
    /// Extracts element info from an already-resolved AutomationElement.
    /// Used for EVENT_OBJECT_FOCUS where AutomationElement.FocusedElement gives the
    /// focused child control rather than the parent window.
    /// </summary>
    public static ElementInfo GetElementInfoFromAutomationElement(AutomationElement el)
    {
        try
        {
            if (el is null) return new ElementInfo(null, null, null, null, false, null);
            var cur = el.Current;
            bool isPassword = cur.IsPassword;
            string? value = null;
            if (!isPassword)
            {
                try
                {
                    if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                        value = ((ValuePattern)vp).Current.Value;
                }
                catch { }
            }
            return new ElementInfo(
                cur.Name,
                cur.AutomationId,
                cur.ClassName,
                cur.ControlType.ProgrammaticName.Replace("ControlType.", ""),
                isPassword,
                isPassword ? null : value
            );
        }
        catch { return new ElementInfo(null, null, null, null, false, null); }
    }

    public static ElementInfo GetElementInfo(IntPtr hwnd)
    {
        try
        {
            var el = AutomationElement.FromHandle(hwnd);
            if (el is null) return new ElementInfo(null, null, null, null, false, null);
            var cur = el.Current;
            bool isPassword = cur.IsPassword;
            string? value = null;
            if (!isPassword)
            {
                try
                {
                    if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                        value = ((ValuePattern)vp).Current.Value;
                }
                catch { }
            }
            return new ElementInfo(
                cur.Name,
                cur.AutomationId,
                cur.ClassName,
                cur.ControlType.ProgrammaticName.Replace("ControlType.", ""),
                isPassword,
                isPassword ? null : value
            );
        }
        catch { return new ElementInfo(null, null, null, null, false, null); }
    }
}
