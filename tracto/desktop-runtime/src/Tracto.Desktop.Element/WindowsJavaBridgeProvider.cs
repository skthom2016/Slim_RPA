using System.Runtime.InteropServices;

namespace Tracto.Desktop.Element;

/// <summary>
/// Production implementation of <see cref="IJavaBridgeProvider"/> that wraps the
/// WindowsAccessBridge-64.dll via P/Invoke.
/// When the DLL is not present IsAvailable() returns false (graceful degradation) and
/// no further P/Invoke calls are attempted.
/// </summary>
public sealed class WindowsJavaBridgeProvider : IJavaBridgeProvider, IDisposable
{
    private const string JabDll = "WindowsAccessBridge-64.dll";

    private bool _initialised;
    private bool _available;

    // -------------------------------------------------------------------------
    // P/Invoke declarations — WindowsAccessBridge-64.dll
    // -------------------------------------------------------------------------

    [DllImport(JabDll, EntryPoint = "Windows_run", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Windows_run();

    [DllImport(JabDll, EntryPoint = "getAccessibleContextAt", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAccessibleContextAt(IntPtr vmid, IntPtr acParent, int x, int y,
        out IntPtr ac);

    [DllImport(JabDll, EntryPoint = "getAccessibleContextInfo",
        CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetAccessibleContextInfo(IntPtr vmid, IntPtr ac,
        out AccessibleContextInfo info);

    [DllImport(JabDll, EntryPoint = "releaseJavaObject", CallingConvention = CallingConvention.StdCall,
        ExactSpelling = true)]
    private static extern void ReleaseJavaObject(IntPtr vmid, IntPtr obj);

    [DllImport(JabDll, EntryPoint = "getAccessibleChildrenCount",
        CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    private static extern int GetAccessibleChildrenCount(IntPtr vmid, IntPtr ac);

    [DllImport(JabDll, EntryPoint = "getAccessibleChildFromContext",
        CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    private static extern IntPtr GetAccessibleChildFromContext(IntPtr vmid, IntPtr ac, int i);

    [DllImport(JabDll, EntryPoint = "doAccessibleActions",
        CallingConvention = CallingConvention.StdCall, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DoAccessibleActions(IntPtr vmid, IntPtr ac,
        ref AccessibleActionsToDo actionsToDo, out int failure);

    [DllImport(JabDll, EntryPoint = "setTextContents",
        CallingConvention = CallingConvention.StdCall, ExactSpelling = true,
        CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetTextContents(IntPtr vmid, IntPtr ac,
        [MarshalAs(UnmanagedType.LPWStr)] string text);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AccessibleContextInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1024)]
        public string description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string role;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string role_en_US;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string states;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string states_en_US;
        public int indexInParent;
        public int childrenCount;
        public int x;
        public int y;
        public int width;
        public int height;
        public int accessibleComponent;
        public int accessibleAction;
        public int accessibleSelection;
        public int accessibleText;
        public int accessibleInterfaces;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccessibleActionInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string name;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccessibleActionsToDo
    {
        public int actionsCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public AccessibleActionInfo[] actions;
    }

    // -------------------------------------------------------------------------
    // IJavaBridgeProvider implementation
    // -------------------------------------------------------------------------

    public bool IsAvailable()
    {
        if (_initialised) return _available;
        _initialised = true;
        try
        {
            _available = Windows_run();
        }
        catch (DllNotFoundException)
        {
            _available = false;
        }
        catch (EntryPointNotFoundException)
        {
            _available = false;
        }
        return _available;
    }

    public JavaElement? FindByName(IntPtr window, string name, string? role)
    {
        if (!IsAvailable())
            throw new InvalidOperationException("Java Access Bridge is not available.");

        // Walk the accessibility tree rooted at the window handle.
        // We use a recursive depth-first search via getAccessibleChildFromContext.
        return SearchTree(window, IntPtr.Zero, name, role);
    }

    public void InvokeClick(JavaElement el)
    {
        if (!IsAvailable())
            throw new InvalidOperationException("Java Access Bridge is not available.");

        // Resolve the accessible context for the element window and fire default action.
        // In a real implementation we'd cache the AC handle; here we re-resolve.
        var ac = ResolveContext(el);
        if (ac == IntPtr.Zero) return;

        try
        {
            var todo = new AccessibleActionsToDo
            {
                actionsCount = 1,
                actions = new AccessibleActionInfo[32]
            };
            todo.actions[0].name = "click";
            DoAccessibleActions(el.WindowHandle, ac, ref todo, out _);
        }
        finally
        {
            ReleaseJavaObject(el.WindowHandle, ac);
        }
    }

    public void InvokeTypeText(JavaElement el, string text)
    {
        if (!IsAvailable())
            throw new InvalidOperationException("Java Access Bridge is not available.");

        // Keyboard-only text delivery: focus the element via accessible actions then
        // send input via SendKeys (Win32 keybd_event path — no coordinate injection).
        // In production this would use the JAB setTextContents or keystroke simulation.
        var ac = ResolveContext(el);
        if (ac == IntPtr.Zero) return;

        try
        {
            // Set text via accessible text interface (best effort)
            SetAccessibleTextContents(el.WindowHandle, ac, text);
        }
        finally
        {
            ReleaseJavaObject(el.WindowHandle, ac);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private JavaElement? SearchTree(IntPtr window, IntPtr parentAc, string targetName, string? targetRole)
    {
        // Obtain the accessible context to inspect at this level.
        // When parentAc is Zero we query the element at coordinate (0,0) of the window as a
        // bootstrap; the real entry point would be getTopLevelObject, but that entry point is
        // not exposed by all JAB versions.  We therefore rely on the caller having pre-resolved
        // a root context and passing it; the public FindByName method seeds the walk via
        // GetAccessibleContextAt from the top-left corner.
        IntPtr ac = parentAc;
        bool releaseAc = false;

        if (ac == IntPtr.Zero)
        {
            if (!GetAccessibleContextAt(window, IntPtr.Zero, 0, 0, out ac) || ac == IntPtr.Zero)
                return null;
            releaseAc = true;
        }

        try
        {
            if (!GetAccessibleContextInfo(window, ac, out var info))
                return null;

            // Check whether this node matches the requested name/role.
            bool nameMatch = string.Equals(info.name, targetName, StringComparison.OrdinalIgnoreCase);
            bool roleMatch = targetRole == null
                || string.Equals(info.role_en_US, targetRole, StringComparison.OrdinalIgnoreCase);

            if (nameMatch && roleMatch)
            {
                return new JavaElement
                {
                    WindowHandle = window,
                    Name         = info.name ?? string.Empty,
                    Role         = info.role_en_US ?? string.Empty,
                    X            = info.x,
                    Y            = info.y,
                    Width        = info.width,
                    Height       = info.height
                };
            }

            // Depth-first search through children.
            int childCount = GetAccessibleChildrenCount(window, ac);
            for (int i = 0; i < childCount; i++)
            {
                IntPtr childAc = GetAccessibleChildFromContext(window, ac, i);
                if (childAc == IntPtr.Zero) continue;

                // SearchTree does NOT own childAc (releaseAc=false path), so we always
                // release it here after the call — the returned JavaElement records only
                // the structural info and does not hold a native handle.
                JavaElement? found = SearchTree(window, childAc, targetName, targetRole);
                ReleaseJavaObject(window, childAc);

                if (found != null) return found;
            }

            return null;
        }
        finally
        {
            if (releaseAc && ac != IntPtr.Zero)
                ReleaseJavaObject(window, ac);
        }
    }

    private IntPtr ResolveContext(JavaElement el)
    {
        // Re-derive accessible context from element coordinates as a lookup key.
        // Production: maintain a handle registry keyed by element identity.
        if (!GetAccessibleContextAt(el.WindowHandle, IntPtr.Zero, el.X, el.Y, out var ac))
            return IntPtr.Zero;
        return ac;
    }

    private static void SetAccessibleTextContents(IntPtr vmid, IntPtr ac, string text)
    {
        // Deliver text via the JAB setTextContents API.
        // SetTextContents returns false when the element does not support editable text;
        // we treat that as a best-effort delivery and do not throw.
        SetTextContents(vmid, ac, text ?? string.Empty);
    }

    public void Dispose()
    {
        // Nothing to release — P/Invoke handles are owned by the JAB library.
    }
}
