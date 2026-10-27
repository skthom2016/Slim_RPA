namespace Tracto.Desktop.Telemetry;

/// <summary>
/// Reference to a captured screenshot evidence file.
/// </summary>
public sealed record EvidenceRef
{
    /// <summary>File URI pointing to the saved screenshot file.</summary>
    public required string FileUri { get; init; }

    /// <summary>True when a sensitive region was detected and successfully masked before saving.</summary>
    public required bool WasMasked { get; init; }

    /// <summary>
    /// When the screenshot was omitted or masking was skipped, describes why.
    /// For example: "masking_unsafe" when ScreenshotMasker.CanMask returned false.
    /// </summary>
    public string? OmittedReason { get; init; }
}
