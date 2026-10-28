namespace TractoRuntime.Browser.Authentication;

/// <summary>
/// Result of a MFA/CAPTCHA or session-expiry detection check (BRW-US-012, WEB-009).
/// </summary>
public sealed record MfaDetectionResult
{
    /// <summary>Gets a value indicating whether the check found an intervention requirement.</summary>
    public bool InterventionRequired { get; init; }

    /// <summary>Identifies the kind of intervention detected.</summary>
    public DetectedInterventionKind Kind { get; init; }

    /// <summary>
    /// Human-readable description of why intervention is required.
    /// Must not contain raw credential values.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// When <see langword="true"/>, the run result should include <c>needsAttention=true</c>
    /// so an authorized human operator can resume it (AC#5, AC#6).
    /// </summary>
    public bool NeedsAttention { get; init; }

    public static MfaDetectionResult None { get; } = new() { InterventionRequired = false };
}

public enum DetectedInterventionKind
{
    None,
    MfaChallenge,
    CaptchaChallenge,
    SessionExpired,
    ConditionalAccessBlocked
}
