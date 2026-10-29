#nullable enable
namespace Tracto.Desktop.Actions;

public static class ProcessDpi
{
    /// <summary>Call once at startup so cursor positions match the physical pixels the Designer records.</summary>
    public static void EnableAwareness() => NativeMethods.EnableDpiAwareness();
}
