using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tracto.Desktop.Input;

/// <summary>
/// Production <see cref="IClipboardApiProvider"/> that delegates to the Win32 clipboard API
/// via P/Invoke.  Must only be used on a Windows interactive desktop thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsClipboardApiProvider : IClipboardApiProvider
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE  = 0x0002;

    // ── P/Invoke declarations ────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "CloseClipboard")]
    private static extern bool CloseClipboardNative();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    // ── IClipboardApiProvider ────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool TryOpenClipboard(IntPtr owner) => OpenClipboard(owner);

    /// <inheritdoc/>
    public void CloseClipboard()
    {
        // Ignore the return value; best-effort close, same as Win32 practice.
        _ = CloseClipboardNative();
    }

    /// <inheritdoc/>
    public string? GetText()
    {
        IntPtr hData = GetClipboardData(CF_UNICODETEXT);
        if (hData == IntPtr.Zero)
            return null;

        IntPtr pData = GlobalLock(hData);
        if (pData == IntPtr.Zero)
            return null;

        try
        {
            return Marshal.PtrToStringUni(pData);
        }
        finally
        {
            GlobalUnlock(hData);
        }
    }

    /// <inheritdoc/>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Include the null terminator in the allocation.
        int byteCount = (text.Length + 1) * sizeof(char);
        IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (nuint)byteCount);
        if (hMem == IntPtr.Zero)
            throw new InvalidOperationException("GlobalAlloc failed for clipboard SetText.");

        IntPtr pMem = GlobalLock(hMem);
        if (pMem == IntPtr.Zero)
            throw new InvalidOperationException("GlobalLock failed for clipboard SetText.");

        try
        {
            Marshal.Copy(text.ToCharArray(), 0, pMem, text.Length);
            // Write null terminator.
            Marshal.WriteInt16(pMem, text.Length * sizeof(char), 0);
        }
        finally
        {
            GlobalUnlock(hMem);
        }

        EmptyClipboard();
        if (SetClipboardData(CF_UNICODETEXT, hMem) == IntPtr.Zero)
            throw new InvalidOperationException("SetClipboardData failed.");
        // Ownership of hMem transfers to the OS after a successful SetClipboardData.
    }
}
