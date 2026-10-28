namespace Tracto.Designer.Infrastructure.Adapters;

using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
using Tracto.Designer.Core.Adapters;

public sealed class WindowsScreenshotAdapter : IScreenshotAdapter
{
    private readonly string _sessionRootDirectory;

    /// <summary>
    /// Creates a WindowsScreenshotAdapter.
    /// </summary>
    /// <param name="sessionRootDirectory">
    /// Root directory under which session sub-folders are created.
    /// When null, defaults to <c>%TEMP%\tracto</c> (for test/dev convenience only;
    /// production registrations must supply an explicit, configured path).
    /// </param>
    public WindowsScreenshotAdapter(string? sessionRootDirectory = null)
    {
        _sessionRootDirectory = sessionRootDirectory
            ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tracto");
    }

    public bool IsAvailable => true;

    public async Task<string?> CaptureAsync(string sessionId, int stepNumber, CancellationToken ct = default)
    {
        string? result = null;

        var app = System.Windows.Application.Current;
        if (app is null)
            return null;

        await app.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                int width = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
                int height = (int)System.Windows.SystemParameters.PrimaryScreenHeight;

                var bitmap = CaptureScreen(width, height);
                if (bitmap is null)
                    return;

                var masked = MaskPasswordFields(bitmap, width, height);

                var dir = System.IO.Path.Combine(_sessionRootDirectory, sessionId);
                System.IO.Directory.CreateDirectory(dir);
                var filename = $"{stepNumber:D3}.png";
                var fullPath = System.IO.Path.Combine(dir, filename);

                using var stream = new System.IO.FileStream(fullPath, System.IO.FileMode.Create);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(masked));
                encoder.Save(stream);

                result = System.IO.Path.Combine(sessionId, filename);
            }
            catch { }
        });

        return result;
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetDesktopWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint GetWindowDC(nint hWnd);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint hdc, int w, int h);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint h);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint hdcDest, int x, int y, int w, int h, nint hdcSrc, int sx, int sy, int rop);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ReleaseDC(nint hWnd, nint hDC);

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    private static BitmapSource? CaptureScreen(int width, int height)
    {
        nint desktopDC = nint.Zero, memDC = nint.Zero, hBitmap = nint.Zero, oldBitmap = nint.Zero;
        try
        {
            desktopDC = GetWindowDC(GetDesktopWindow());
            memDC = CreateCompatibleDC(desktopDC);
            hBitmap = CreateCompatibleBitmap(desktopDC, width, height);
            oldBitmap = SelectObject(memDC, hBitmap);
            const int SRCCOPY = 0x00CC0020;
            if (!BitBlt(memDC, 0, 0, width, height, desktopDC, 0, 0, SRCCOPY))
                return null; // locked desktop or DWM protection — do not produce a blank image
            var src = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, nint.Zero,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally
        {
            if (oldBitmap != nint.Zero) SelectObject(memDC, oldBitmap);
            if (hBitmap != nint.Zero) DeleteObject(hBitmap);
            if (memDC != nint.Zero) DeleteDC(memDC);
            if (desktopDC != nint.Zero) ReleaseDC(GetDesktopWindow(), desktopDC);
        }
    }

    private static BitmapSource MaskPasswordFields(BitmapSource source, int width, int height)
    {
        try
        {
            var condition = new PropertyCondition(AutomationElement.IsPasswordProperty, true);
            var root = AutomationElement.RootElement;
            var passwordFields = root.FindAll(TreeScope.Descendants, condition);

            if (passwordFields.Count == 0)
                return source;

            // Convert to WriteableBitmap for pixel manipulation
            var wb = new WriteableBitmap(source);
            wb.Lock();
            try
            {
                foreach (AutomationElement el in passwordFields)
                {
                    try
                    {
                        var r = el.Current.BoundingRectangle;
                        if (r.IsEmpty) continue;

                        int left = Math.Max(0, (int)r.Left);
                        int top = Math.Max(0, (int)r.Top);
                        int right = Math.Min(width, (int)(r.Left + r.Width));
                        int bottom = Math.Min(height, (int)(r.Top + r.Height));

                        for (int py = top; py < bottom; py++)
                        {
                            for (int px = left; px < right; px++)
                            {
                                nint ptr = wb.BackBuffer + py * wb.BackBufferStride + px * 4;
                                System.Runtime.InteropServices.Marshal.WriteInt32(ptr, unchecked((int)0xFF000000));
                            }
                        }

                        wb.AddDirtyRect(new System.Windows.Int32Rect(left, top, right - left, bottom - top));
                    }
                    catch { }
                }
            }
            finally
            {
                wb.Unlock();
            }

            wb.Freeze();
            return wb;
        }
        catch
        {
            return source;
        }
    }
}
