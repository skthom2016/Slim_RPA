namespace Tracto.Designer.Infrastructure.Desktop;

using System.Text;

[Flags]
internal enum KeyModifiers { None = 0, Shift = 1, Ctrl = 2, Alt = 4, Win = 8 }

/// <summary>
/// Turns a raw key-down (virtual-key code plus held modifiers) into the key vocabulary the
/// runtime understands: a single character for printable keys, "Ctrl+S" / "Enter" style names
/// for shortcuts and special keys, and "{LWIN}s" for Windows-key combinations.
/// </summary>
internal static class KeyNameTranslator
{
    public static KeyModifiers ModifierOf(uint vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => KeyModifiers.Shift,
        0x11 or 0xA2 or 0xA3 => KeyModifiers.Ctrl,
        0x12 or 0xA4 or 0xA5 => KeyModifiers.Alt,
        0x5B or 0x5C         => KeyModifiers.Win,
        _                    => KeyModifiers.None,
    };

    public static string Translate(uint vk, uint scanCode, KeyModifiers mods, bool capsLockOn, IntPtr layout)
    {
        var special = SpecialName(vk);

        if (mods.HasFlag(KeyModifiers.Win))
        {
            var ch = special is null ? PrintableChar(vk, scanCode, KeyModifiers.None, false, layout) : null;
            return "{LWIN}" + (ch?.ToLowerInvariant() ?? string.Empty);
        }

        if (mods.HasFlag(KeyModifiers.Ctrl) || mods.HasFlag(KeyModifiers.Alt))
        {
            var name = special ?? PrintableChar(vk, scanCode, KeyModifiers.None, false, layout)?.ToUpperInvariant();
            return name is null ? $"VK_{vk:X2}" : Prefix(mods) + name;
        }

        if (special is not null)
            return mods.HasFlag(KeyModifiers.Shift) ? "Shift+" + special : special;

        return PrintableChar(vk, scanCode, mods, capsLockOn, layout) ?? $"VK_{vk:X2}";
    }

    private static string Prefix(KeyModifiers mods)
    {
        var sb = new StringBuilder();
        if (mods.HasFlag(KeyModifiers.Ctrl))  sb.Append("Ctrl+");
        if (mods.HasFlag(KeyModifiers.Alt))   sb.Append("Alt+");
        if (mods.HasFlag(KeyModifiers.Shift)) sb.Append("Shift+");
        return sb.ToString();
    }

    private static string? SpecialName(uint vk) => vk switch
    {
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Esc",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x2D => "Insert",
        0x2E => "Delete",
        >= 0x70 and <= 0x7B => "F" + (vk - 0x6F),
        _ => null,
    };

    private static string? PrintableChar(uint vk, uint scanCode, KeyModifiers mods, bool capsLockOn, IntPtr layout)
    {
        if (vk == 0x20) return " ";

        var state = new byte[256];
        if (mods.HasFlag(KeyModifiers.Shift)) state[0x10] = 0x80;
        if (capsLockOn) state[0x14] = 0x01;

        var buf = new StringBuilder(8);
        // Flag 4: do not change the keyboard state (dead keys stay untouched).
        var n = NativeInterop.ToUnicodeEx(vk, scanCode, state, buf, buf.Capacity, 4, layout);
        return n == 1 && !char.IsControl(buf[0]) ? buf.ToString(0, 1) : null;
    }
}
