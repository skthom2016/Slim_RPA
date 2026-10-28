namespace Tracto.Designer.Core.Adapters;

public interface IScreenshotAdapter
{
    bool IsAvailable { get; }
    /// <summary>Returns relative file path saved to session dir, or null if unavailable/policy-denied.</summary>
    Task<string?> CaptureAsync(string sessionId, int stepNumber, CancellationToken ct = default);
}
