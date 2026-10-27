using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Tracto.Desktop.Input;

/// <summary>
/// Production implementation of <see cref="IKeySendInputProvider"/> using Win32 SendInput.
/// </summary>
public sealed class WindowsKeySendInputProvider : IKeySendInputProvider
{
    // Win32 constants
    private const uint INPUT_KEYBOARD    = 1;
    private const uint KEYEVENTF_KEYUP   = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort Vk;
        public ushort Scan;
        public uint   Flags;
        public uint   Time;
        public nint   ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint        Type;
        public KEYBDINPUT  Ki;
        // Padding to match union size (mouse/hardware inputs may be larger on 64-bit).
        private ulong _pad1;
        private ulong _pad2;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    private static void Send(ushort vk, ushort scan, uint flags)
    {
        var input = new INPUT
        {
            Type = INPUT_KEYBOARD,
            Ki   = new KEYBDINPUT { Vk = vk, Scan = scan, Flags = flags }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    private static ushort NameToVk(string key) => key.ToUpperInvariant() switch
    {
        "CTRL"  or "CONTROL" => (ushort)Keys.ControlKey,
        "ALT"                => (ushort)Keys.Menu,
        "SHIFT"              => (ushort)Keys.ShiftKey,
        "WIN"   or "WINDOWS" => (ushort)Keys.LWin,
        "ENTER" or "RETURN"  => (ushort)Keys.Return,
        "TAB"                => (ushort)Keys.Tab,
        "ESC"   or "ESCAPE"  => (ushort)Keys.Escape,
        "SPACE"              => (ushort)Keys.Space,
        "BACK"  or "BACKSPACE" => (ushort)Keys.Back,
        "DELETE" or "DEL"    => (ushort)Keys.Delete,
        "HOME"               => (ushort)Keys.Home,
        "END"                => (ushort)Keys.End,
        "PAGEUP"             => (ushort)Keys.PageUp,
        "PAGEDOWN"           => (ushort)Keys.PageDown,
        "LEFT"               => (ushort)Keys.Left,
        "RIGHT"              => (ushort)Keys.Right,
        "UP"                 => (ushort)Keys.Up,
        "DOWN"               => (ushort)Keys.Down,
        "F1"  => (ushort)Keys.F1,  "F2"  => (ushort)Keys.F2,
        "F3"  => (ushort)Keys.F3,  "F4"  => (ushort)Keys.F4,
        "F5"  => (ushort)Keys.F5,  "F6"  => (ushort)Keys.F6,
        "F7"  => (ushort)Keys.F7,  "F8"  => (ushort)Keys.F8,
        "F9"  => (ushort)Keys.F9,  "F10" => (ushort)Keys.F10,
        "F11" => (ushort)Keys.F11, "F12" => (ushort)Keys.F12,
        _ when key.Length == 1 => (ushort)(VkKeyScan(key[0]) & 0xFF),
        _ => throw new ArgumentException($"Unknown key name: '{key}'", nameof(key))
    };

    public void KeyDown(string key) => Send(NameToVk(key), 0, 0);
    public void KeyUp(string key)   => Send(NameToVk(key), 0, KEYEVENTF_KEYUP);

    public void CharDown(char ch)
    {
        var input = new INPUT
        {
            Type = INPUT_KEYBOARD,
            Ki   = new KEYBDINPUT { Vk = 0, Scan = ch, Flags = KEYEVENTF_UNICODE }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public void CharUp(char ch)
    {
        var input = new INPUT
        {
            Type = INPUT_KEYBOARD,
            Ki   = new KEYBDINPUT { Vk = 0, Scan = ch, Flags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP }
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public void SetClipboardText(string text)
    {
        // Must run on STA thread for OLE clipboard access.
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                System.Windows.Forms.Clipboard.SetText(text);
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        tcs.Task.GetAwaiter().GetResult();
    }

    public void SendPaste()
    {
        // Ctrl down, V down, V up, Ctrl up
        ushort vkCtrl = NameToVk("Ctrl");
        ushort vkV    = NameToVk("V");
        Send(vkCtrl, 0, 0);
        Send(vkV,    0, 0);
        Send(vkV,    0, KEYEVENTF_KEYUP);
        Send(vkCtrl, 0, KEYEVENTF_KEYUP);
    }
}
