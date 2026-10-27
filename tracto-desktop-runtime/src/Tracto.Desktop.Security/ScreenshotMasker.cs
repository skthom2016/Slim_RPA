using System.Drawing;

namespace Tracto.Desktop.Security;

/// <summary>
/// Masks sensitive regions in screenshot bitmaps to prevent credential exposure in evidence.
/// </summary>
public static class ScreenshotMasker
{
    /// <summary>
    /// Blacks out the given <paramref name="region"/> in <paramref name="bmp"/> in place.
    /// No-op when <see cref="CanMask"/> returns false for the region.
    /// </summary>
    /// <param name="bmp">The bitmap to modify in place.</param>
    /// <param name="region">The rectangular region to black out.</param>
    public static void MaskRegion(Bitmap bmp, Rectangle region)
    {
        ArgumentNullException.ThrowIfNull(bmp);
        if (!CanMask(region, bmp.Size))
            return;

        using var g = Graphics.FromImage(bmp);
        g.FillRectangle(Brushes.Black, region);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="sensitiveRegion"/> lies entirely within the
    /// bounds of an image with size <paramref name="imageSize"/> and has positive area.
    /// </summary>
    /// <param name="sensitiveRegion">The region to test.</param>
    /// <param name="imageSize">The dimensions of the image.</param>
    public static bool CanMask(Rectangle sensitiveRegion, Size imageSize)
    {
        if (sensitiveRegion.Width <= 0 || sensitiveRegion.Height <= 0)
            return false;
        if (sensitiveRegion.X < 0 || sensitiveRegion.Y < 0)
            return false;
        if (sensitiveRegion.Right > imageSize.Width || sensitiveRegion.Bottom > imageSize.Height)
            return false;
        return true;
    }
}
