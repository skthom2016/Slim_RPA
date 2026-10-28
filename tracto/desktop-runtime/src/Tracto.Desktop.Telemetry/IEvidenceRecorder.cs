namespace Tracto.Desktop.Telemetry;

/// <summary>
/// Contract for capturing and, when required, masking screenshot evidence for a step.
/// </summary>
public interface IEvidenceRecorder
{
    /// <summary>
    /// Captures a screenshot for the specified step.
    /// When a sensitive region is present the implementation must mask it or decline to capture.
    /// </summary>
    /// <param name="stepId">The step identifier this evidence belongs to.</param>
    /// <param name="sensitiveRegionPresent">
    /// True when the active UI region contains sensitive information (e.g. a password field).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// An <see cref="EvidenceRef"/> describing the captured file, or <c>null</c> when capture
    /// was skipped entirely (e.g. no display available).
    /// </returns>
    Task<EvidenceRef?> RecordScreenshotAsync(string stepId, bool sensitiveRegionPresent, CancellationToken ct);
}
