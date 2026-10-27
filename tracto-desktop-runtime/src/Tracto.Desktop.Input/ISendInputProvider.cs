using System.Runtime.InteropServices;

namespace Tracto.Desktop.Input;

/// <summary>
/// Thin abstraction over Win32 SendInput so production and test code differ only in the implementation.
/// </summary>
public interface ISendInputProvider
{
    /// <summary>
    /// Sends an array of mouse input structures to the OS input queue.
    /// </summary>
    void SendMouseInput(MouseInput[] inputs);
}

/// <summary>
/// Mirrors the Win32 INPUT structure (mouse variant).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MouseInput
{
    public int Type;        // INPUT_MOUSE = 0
    public int Dx;
    public int Dy;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public nint ExtraInfo;
}

/// <summary>
/// MOUSEEVENTF_* flag constants used when composing mouse input.
/// </summary>
public static class MouseEventFlags
{
    public const uint MOVE            = 0x0001;
    public const uint LEFTDOWN        = 0x0002;
    public const uint LEFTUP          = 0x0004;
    public const uint RIGHTDOWN       = 0x0008;
    public const uint RIGHTUP         = 0x0010;
    public const uint MIDDLEDOWN      = 0x0020;
    public const uint MIDDLEUP        = 0x0040;
    public const uint WHEEL           = 0x0800;
    public const uint ABSOLUTE        = 0x8000;
}
