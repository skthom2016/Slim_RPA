#nullable enable
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace Tracto.Desktop.Actions.Handlers;

/// <summary>
/// Sends a keyboard shortcut or key press to the active window.
///
/// Action name: key
/// Inputs:
///   key     (string, required) — key description, e.g. "Enter", "Tab", "Ctrl+S", "Alt+F4"
///   window  (string, optional) — window title hint to activate first
/// Outputs:
///   sendKeysValue (string) — the SendKeys string that was sent
/// </summary>
public sealed class KeyHandler : IActionHandler
{
    private readonly ILogger<KeyHandler> _logger;

    public string ActionName => "key";

    public KeyHandler(ILogger<KeyHandler> logger) => _logger = logger;

    public async Task<ActionResult> ExecuteAsync(ActionContext ctx)
    {
        var key    = ctx.GetString("key");
        var window = ctx.GetString("window");

        if (string.IsNullOrWhiteSpace(key))
            return ActionResult.Fail("CONFIGURATION", "Input 'key' is required for key action");

        if (!string.IsNullOrWhiteSpace(window))
        {
            var hwnd = NativeMethods.FindWindowByTitle(window);
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(hwnd);
                await Task.Delay(300, ctx.CancellationToken);
            }
        }

        // {LWIN} and {RWIN} are not supported by SendKeys — use keybd_event directly.
        if (key.StartsWith("{LWIN}", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("{WIN}", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("{RWIN}", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(100, ctx.CancellationToken);
            var trailer = key.Length > (key.StartsWith("{WIN}", StringComparison.OrdinalIgnoreCase) ? 5 : 6)
                ? key[(key.IndexOf('}') + 1)..] : string.Empty;
            if (trailer.Length == 1)
                NativeMethods.PressWinPlusKey(trailer[0]);
            else
                NativeMethods.PressWinKey();
            await Task.Delay(150, ctx.CancellationToken);
            _logger.LogInformation("key: '{Key}' → Win32 keybd_event", key);
            return ActionResult.Ok(new Dictionary<string, object?> { ["sendKeysValue"] = key });
        }

        var sendKeysStr = ToSendKeys(key);
        await Task.Delay(100, ctx.CancellationToken);
        SendKeys.SendWait(sendKeysStr);
        await Task.Delay(150, ctx.CancellationToken);

        _logger.LogInformation("key: '{Key}' → '{SendKeys}'", key, sendKeysStr);
        return ActionResult.Ok(new Dictionary<string, object?> { ["sendKeysValue"] = sendKeysStr });
    }

    /// <summary>
    /// Maps a human-readable key description to SendKeys syntax.
    /// Handles patterns like "Ctrl+S", "Alt+F4", "Shift+Tab", "Enter", "F5", etc.
    /// </summary>
    private static string ToSendKeys(string key)
    {
        // Raw virtual-key name as written by the Designer recorder, e.g. "VK_4E".
        if (TryParseVirtualKey(key, out var vk))
            return VirtualKeyToSendKeys(vk);

        // Already looks like a SendKeys string (contains ^ % + { }) — pass through.
        if (key.Length > 0 && "^%+{~".Contains(key[0]))
            return key;

        // Split on '+' to separate modifiers from the base key.
        var parts    = key.Split('+', StringSplitOptions.RemoveEmptyEntries);
        var baseKey  = parts[^1].Trim();
        var mods     = parts.Length > 1 ? parts[..^1] : [];

        var modStr = string.Concat(mods.Select(m => m.ToLowerInvariant() switch
        {
            "ctrl" or "control" => "^",
            "alt"               => "%",
            "shift"             => "+",
            _                   => string.Empty,
        }));

        var keyStr = baseKey.ToUpperInvariant() switch
        {
            "ENTER" or "RETURN"               => "{ENTER}",
            "TAB"                             => "{TAB}",
            "ESCAPE" or "ESC"                 => "{ESC}",
            "DELETE" or "DEL"                 => "{DELETE}",
            "BACKSPACE" or "BACK" or "BS"     => "{BACKSPACE}",
            "INSERT" or "INS"                 => "{INSERT}",
            "HOME"                            => "{HOME}",
            "END"                             => "{END}",
            "PAGEUP" or "PGUP"               => "{PGUP}",
            "PAGEDOWN" or "PGDN" or "PGDOWN" => "{PGDN}",
            "UP"                              => "{UP}",
            "DOWN"                            => "{DOWN}",
            "LEFT"                            => "{LEFT}",
            "RIGHT"                           => "{RIGHT}",
            "F1"  => "{F1}",  "F2"  => "{F2}",  "F3"  => "{F3}",  "F4"  => "{F4}",
            "F5"  => "{F5}",  "F6"  => "{F6}",  "F7"  => "{F7}",  "F8"  => "{F8}",
            "F9"  => "{F9}",  "F10" => "{F10}", "F11" => "{F11}", "F12" => "{F12}",
            "SPACE" or " "                    => " ",
            // Single letter/digit — lowercase for SendKeys Ctrl combos (^s not ^S)
            { Length: 1 }                     => modStr.Contains('^') || modStr.Contains('%')
                                                    ? baseKey.ToLowerInvariant()
                                                    : NativeMethods.EscapeForSendKeys(baseKey),
            _                                 => NativeMethods.EscapeForSendKeys(baseKey),
        };

        return modStr + keyStr;
    }

    private static bool TryParseVirtualKey(string key, out int vk)
    {
        vk = 0;
        return key.Length is >= 4 and <= 5
            && key.StartsWith("VK_", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(key.AsSpan(3), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out vk);
    }

    /// <summary>
    /// Maps a Windows virtual-key code to a SendKeys string. Modifier keys alone
    /// map to an empty string because a recorded key-down carries no hold state.
    /// </summary>
    private static string VirtualKeyToSendKeys(int vk) => vk switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString().ToLowerInvariant(),
        >= 0x60 and <= 0x69                        => ((char)('0' + (vk - 0x60))).ToString(),
        >= 0x70 and <= 0x7B                        => "{F" + (vk - 0x6F) + "}",
        0x08 => "{BACKSPACE}",
        0x09 => "{TAB}",
        0x0D => "{ENTER}",
        0x1B => "{ESC}",
        0x20 => " ",
        0x21 => "{PGUP}",
        0x22 => "{PGDN}",
        0x23 => "{END}",
        0x24 => "{HOME}",
        0x25 => "{LEFT}",
        0x26 => "{UP}",
        0x27 => "{RIGHT}",
        0x28 => "{DOWN}",
        0x2D => "{INSERT}",
        0x2E => "{DELETE}",
        _    => string.Empty,
    };
}
