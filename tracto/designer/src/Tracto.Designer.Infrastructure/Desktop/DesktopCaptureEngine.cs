namespace Tracto.Designer.Infrastructure.Desktop;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using Tracto.Designer.Core.Desktop;
using Tracto.Designer.Core.Recording;

public sealed class DesktopCaptureEngine : IDesktopCaptureEngine
{
    private readonly IRecordingSessionService _session;
    private long _sequence;
    private bool _running;
    private IntPtr _winEventHook, _kbHook, _mouseHook;
    private KeyModifiers _heldModifiers;

    // MUST be fields — local delegates get GC'd and crash the hook
    private readonly NativeInterop.WinEventDelegate _winEventDelegate;
    private readonly NativeInterop.HookProc _kbDelegate;
    private readonly NativeInterop.HookProc _mouseDelegate;

    public bool IsRunning => _running;
    public event EventHandler<RawEvent>? EventCaptured;

    public DesktopCaptureEngine(IRecordingSessionService session)
    {
        _session = session;
        _winEventDelegate = OnWinEvent;
        _kbDelegate = OnKeyboard;
        _mouseDelegate = OnMouse;
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _winEventHook = NativeInterop.SetWinEventHook(
            NativeInterop.EVENT_SYSTEM_FOREGROUND,
            NativeInterop.EVENT_OBJECT_FOCUS,
            IntPtr.Zero,
            _winEventDelegate,
            0, 0,
            NativeInterop.WINEVENT_OUTOFCONTEXT);
        var hMod = NativeInterop.GetModuleHandle(null);
        _kbHook    = NativeInterop.SetWindowsHookEx(NativeInterop.WH_KEYBOARD_LL, _kbDelegate,    hMod, 0);
        _mouseHook = NativeInterop.SetWindowsHookEx(NativeInterop.WH_MOUSE_LL,    _mouseDelegate, hMod, 0);
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        if (_winEventHook != IntPtr.Zero) { NativeInterop.UnhookWinEvent(_winEventHook); _winEventHook = IntPtr.Zero; }
        if (_kbHook != IntPtr.Zero)       { NativeInterop.UnhookWindowsHookEx(_kbHook);    _kbHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero)    { NativeInterop.UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
    }

    private void OnWinEvent(IntPtr hHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            // AC-01: filter out every event code except the two we care about
            if (eventType != NativeInterop.EVENT_SYSTEM_FOREGROUND && eventType != NativeInterop.EVENT_OBJECT_FOCUS) return;
            if (!_running || hwnd == IntPtr.Zero) return;
            var sb = new StringBuilder(256);
            NativeInterop.GetWindowText(hwnd, sb, 256);
            NativeInterop.GetWindowThreadProcessId(hwnd, out uint pid);
            string procName = "unknown";
            try { procName = Process.GetProcessById((int)pid).ProcessName; } catch { }
            // AC-02: for Focus events, resolve the focused child element via UIA rather than the
            // parent hwnd (which reflects the window, not the focused button/field).
            UiaCapture.ElementInfo uia;
            if (eventType == NativeInterop.EVENT_OBJECT_FOCUS)
            {
                try
                {
                    var focused = AutomationElement.FocusedElement;
                    uia = focused != null ? UiaCapture.GetElementInfoFromAutomationElement(focused) : new UiaCapture.ElementInfo(null, null, null, null, false, null);
                }
                catch { uia = new UiaCapture.ElementInfo(null, null, null, null, false, null); }
            }
            else
            {
                uia = UiaCapture.GetElementInfo(hwnd);
            }
            var evtType = eventType == NativeInterop.EVENT_SYSTEM_FOREGROUND
                ? RawEventType.WindowActivate
                : RawEventType.Focus;
            var raw = new RawEvent(
                System.Threading.Interlocked.Increment(ref _sequence),
                DateTimeOffset.Now,
                evtType,
                procName,
                sb.ToString(),
                uia.Name,
                uia.AutomationId,
                uia.ClassName,
                uia.IsPassword ? null : uia.Value,
                uia.IsPassword,
                null,
                0, 0);
            Dispatch(raw);
        }
        catch { }
    }

    private IntPtr OnMouse(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            var next = NativeInterop.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            if (nCode == NativeInterop.HC_ACTION && _running)
            {
                var ms = Marshal.PtrToStructure<NativeInterop.MSLLHOOKSTRUCT>(lParam);
                RawEventType? evtType = (int)wParam switch
                {
                    NativeInterop.WM_LBUTTONDOWN   => RawEventType.Click,
                    NativeInterop.WM_RBUTTONDOWN   => RawEventType.RightClick,
                    NativeInterop.WM_LBUTTONDBLCLK => RawEventType.DoubleClick,
                    _ => null
                };
                if (evtType.HasValue)
                {
                    var target = DescribeClickTarget(ms.pt.x, ms.pt.y);
                    var raw = new RawEvent(
                        System.Threading.Interlocked.Increment(ref _sequence),
                        DateTimeOffset.Now,
                        evtType.Value,
                        string.Empty,
                        target.WindowTitle,
                        target.ControlName, null, null, null,
                        false, null,
                        ms.pt.x, ms.pt.y,
                        target.OffsetX, target.OffsetY);
                    Dispatch(raw);
                }
            }
            return next;
        }
        catch { return NativeInterop.CallNextHookEx(_mouseHook, nCode, wParam, lParam); }
    }

    private IntPtr OnKeyboard(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            var next = NativeInterop.CallNextHookEx(_kbHook, nCode, wParam, lParam);
            if (nCode == NativeInterop.HC_ACTION && _running)
            {
                var msg = (int)wParam;
                var kb = Marshal.PtrToStructure<NativeInterop.KBDLLHOOKSTRUCT>(lParam);
                var isDown = msg == NativeInterop.WM_KEYDOWN || msg == NativeInterop.WM_SYSKEYDOWN;
                var isUp   = msg == NativeInterop.WM_KEYUP   || msg == NativeInterop.WM_SYSKEYUP;

                // Modifiers are tracked as held state and folded into the next key, not recorded alone.
                var modifier = KeyNameTranslator.ModifierOf(kb.vkCode);
                if (modifier != KeyModifiers.None)
                {
                    if (isDown) _heldModifiers |= modifier;
                    else if (isUp) _heldModifiers &= ~modifier;
                    return next;
                }
                if (!isDown) return next;

                bool isPassword = false;
                try
                {
                    var focused = AutomationElement.FocusedElement;
                    if (focused != null) isPassword = focused.Current.IsPassword;
                }
                catch { }
                string? keyVal = isPassword ? null : KeyNameTranslator.Translate(
                    kb.vkCode, kb.scanCode, _heldModifiers,
                    (NativeInterop.GetKeyState(0x14) & 1) != 0, ForegroundKeyboardLayout());
                var raw = new RawEvent(
                    System.Threading.Interlocked.Increment(ref _sequence),
                    DateTimeOffset.Now,
                    RawEventType.KeyPress,
                    string.Empty,
                    string.Empty,
                    null, null, null, null,
                    isPassword, keyVal,
                    0, 0);
                Dispatch(raw);
            }
            return next;
        }
        catch { return NativeInterop.CallNextHookEx(_kbHook, nCode, wParam, lParam); }
    }

    private readonly record struct ClickTarget(string WindowTitle, string? ControlName, int? OffsetX, int? OffsetY);

    /// <summary>
    /// Identifies what was clicked: the top-level window, the click position relative to that
    /// window's top-left corner, and (best effort, time-boxed so the mouse hook stays responsive)
    /// the UI element under the cursor.
    /// </summary>
    private static ClickTarget DescribeClickTarget(int x, int y)
    {
        try
        {
            var hwnd = NativeInterop.WindowFromPoint(new NativeInterop.POINT { x = x, y = y });
            if (hwnd == IntPtr.Zero) return default;
            var root = NativeInterop.GetAncestor(hwnd, NativeInterop.GA_ROOT);
            if (root == IntPtr.Zero) root = hwnd;

            var title = new StringBuilder(256);
            NativeInterop.GetWindowText(root, title, title.Capacity);
            int? offX = null, offY = null;
            if (NativeInterop.GetWindowRect(root, out var rect))
            {
                offX = x - rect.Left;
                offY = y - rect.Top;
            }

            string? name = null;
            var lookup = Task.Run(() =>
            {
                try { return AutomationElement.FromPoint(new System.Windows.Point(x, y))?.Current.Name; }
                catch { return null; }
            });
            if (lookup.Wait(150)) name = lookup.Result;

            return new ClickTarget(title.ToString(), string.IsNullOrWhiteSpace(name) ? null : name, offX, offY);
        }
        catch { return default; }
    }

    private static IntPtr ForegroundKeyboardLayout()
    {
        var thread = NativeInterop.GetWindowThreadProcessId(NativeInterop.GetForegroundWindow(), out _);
        return NativeInterop.GetKeyboardLayout(thread);
    }

    private void Dispatch(RawEvent raw)
    {
        EventCaptured?.Invoke(this, raw);
        var step = _session.Transcript.Count + 1;
        var entry = new TranscriptEntry(step, raw.Timestamp, DescribeEvent(raw), raw, null, null);
        _session.AppendTranscriptEntry(entry);
    }

    private static string DescribeEvent(RawEvent r) => r.EventType switch
    {
        RawEventType.Click          => $"Clicked {r.ControlName ?? "element"} in {r.WindowTitle}",
        RawEventType.RightClick     => $"Right-clicked in {r.WindowTitle}",
        RawEventType.DoubleClick    => $"Double-clicked in {r.WindowTitle}",
        RawEventType.WindowActivate => $"Activated window: {r.WindowTitle}",
        RawEventType.Focus          => $"Focused: {r.ControlName ?? "element"} in {r.WindowTitle}",
        RawEventType.KeyPress       => r.IsPasswordField ? "Key pressed (password field)" : $"Key pressed: {r.KeyValue}",
        _ => $"{r.EventType} in {r.WindowTitle}"
    };

    public void Dispose() => Stop();
}
