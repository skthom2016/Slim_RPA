using System.Drawing;
using System.Drawing.Imaging;
using Tracto.Desktop.Security;

namespace Tracto.Desktop.Telemetry;

/// <summary>
/// Records screenshot evidence with mandatory masking when a sensitive region is present.
/// Delegates masking capability checks to <see cref="ScreenshotMasker"/>.
/// </summary>
public sealed class SafeEvidenceRecorder : IEvidenceRecorder
{
    private readonly string _evidenceRootDir;

    /// <summary>
    /// The sensitive region to mask. When null a full-screen bounding region is used as a safety fallback.
    /// </summary>
    public Rectangle? SensitiveRegion { get; init; }

    /// <summary>
    /// The size of the screen / virtual canvas used to validate masking.
    /// Defaults to (1920, 1080) when not set.
    /// </summary>
    public Size ScreenSize { get; init; } = new Size(1920, 1080);

    /// <param name="evidenceRootDir">
    /// Root directory under which evidence files are saved.
    /// Files are written to <c>{evidenceRootDir}\evidence\{stepId}_{timestamp}.png</c>.
    /// </param>
    public SafeEvidenceRecorder(string evidenceRootDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRootDir);
        _evidenceRootDir = evidenceRootDir;
    }

    /// <inheritdoc />
    public Task<EvidenceRef?> RecordScreenshotAsync(
        string stepId,
        bool sensitiveRegionPresent,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);

        if (sensitiveRegionPresent)
        {
            var region = SensitiveRegion ?? new Rectangle(0, 0, ScreenSize.Width, ScreenSize.Height);
            bool canMask = ScreenshotMasker.CanMask(region, ScreenSize);

            if (!canMask)
            {
                // Masking is unsafe — return a reference indicating the omission reason.
                return Task.FromResult<EvidenceRef?>(new EvidenceRef
                {
                    FileUri    = string.Empty,
                    WasMasked  = false,
                    OmittedReason = "masking_unsafe",
                });
            }

            // Masking is safe: create a blank bitmap representing the captured screenshot,
            // mask the region, and persist it.
            return Task.FromResult<EvidenceRef?>(CaptureAndMask(stepId, region));
        }

        // No sensitive region: capture without masking.
        return Task.FromResult<EvidenceRef?>(CaptureWithoutMask(stepId));
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private EvidenceRef CaptureAndMask(string stepId, Rectangle region)
    {
        using var bmp = CreatePlaceholderBitmap();
        ScreenshotMasker.MaskRegion(bmp, region);
        var path = SaveBitmap(bmp, stepId);
        return new EvidenceRef
        {
            FileUri   = new Uri(path).AbsoluteUri,
            WasMasked = true,
        };
    }

    private EvidenceRef CaptureWithoutMask(string stepId)
    {
        using var bmp = CreatePlaceholderBitmap();
        var path = SaveBitmap(bmp, stepId);
        return new EvidenceRef
        {
            FileUri   = new Uri(path).AbsoluteUri,
            WasMasked = false,
        };
    }

    private Bitmap CreatePlaceholderBitmap()
    {
        // In a real implementation this would capture the live desktop.
        // In this implementation we create a minimal white bitmap so no screen APIs are required.
        return new Bitmap(ScreenSize.Width, ScreenSize.Height, PixelFormat.Format32bppArgb);
    }

    private string SaveBitmap(Bitmap bmp, string stepId)
    {
        var dir = Path.Combine(_evidenceRootDir, "evidence");
        Directory.CreateDirectory(dir);
        var filename = $"{SanitiseStepId(stepId)}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.png";
        var path = Path.Combine(dir, filename);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    private static string SanitiseStepId(string stepId) =>
        string.Concat(stepId.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
}
