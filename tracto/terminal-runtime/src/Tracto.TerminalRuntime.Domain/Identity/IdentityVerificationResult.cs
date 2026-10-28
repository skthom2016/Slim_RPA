namespace Tracto.TerminalRuntime.Domain.Identity;

/// <summary>
/// Holds the verified identity information returned after a successful check.
/// </summary>
/// <param name="VerifiedIdentity">
///   The actual identity that was confirmed (Windows SAM name or Linux username).
///   This value must never be logged alongside secret material.
/// </param>
/// <param name="Kind">Whether the verified identity is Windows or WSL.</param>
public sealed record IdentityVerificationResult(
    string VerifiedIdentity,
    IdentityVerificationKind Kind);

/// <summary>Distinguishes the type of identity that was verified.</summary>
public enum IdentityVerificationKind
{
    Windows,
    Wsl
}
