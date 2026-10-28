using Microsoft.Playwright;

namespace TractoRuntime.Browser.Authentication;

/// <summary>
/// Detects MFA/CAPTCHA challenges and expired-session conditions during authentication
/// flows (BRW-US-012, WEB-009).
/// <list type="bullet">
///   <item>AC#5 — MFA or CAPTCHA challenge → run stops with FAILED or needsAttention=true;
///               no bypass is attempted.</item>
///   <item>AC#6 — Expired storageState / session requiring re-authentication → run stops with
///               needsAttention=true or FAILED; no silent credential re-use.</item>
/// </list>
/// </summary>
public interface IMfaAndSessionDetector
{
    /// <summary>
    /// Inspects <paramref name="page"/> for signs of an MFA challenge, CAPTCHA,
    /// conditional-access block, or expired session.
    /// </summary>
    /// <returns>
    /// A <see cref="MfaDetectionResult"/> describing whether intervention is required and why.
    /// </returns>
    Task<MfaDetectionResult> DetectAsync(IPage page, CancellationToken ct);
}
