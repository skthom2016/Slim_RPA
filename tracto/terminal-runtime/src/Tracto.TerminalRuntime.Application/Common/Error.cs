namespace Tracto.TerminalRuntime.Application.Common;

/// <summary>
/// Discriminated error record used with Result&lt;T,Error&gt;.
/// Carries an error code, human-readable message, and optional detail code for Orchestrator payloads.
/// </summary>
public sealed record Error(
    ErrorKind Kind,
    string Code,
    string Message,
    string? DetailCode = null)
{
    // ── factories ────────────────────────────────────────────────────────────

    public static Error General(string code, string message, string? detailCode = null) =>
        new(ErrorKind.General, code, message, detailCode);

    public static Error Validation(string code, string message, string? detailCode = null) =>
        new(ErrorKind.Validation, code, message, detailCode);

    public static Error Environment(string code, string message, string? detailCode = null) =>
        new(ErrorKind.Environment, code, message, detailCode);

    /// <summary>Creates an identity or credential mismatch error (AC-1, AC-2).</summary>
    public static Error Authentication(string code, string message, string? detailCode = null) =>
        new(ErrorKind.Authentication, code, message, detailCode);

    /// <summary>Creates a policy-denied error (AC-3, AC-4).</summary>
    public static Error PolicyDenied(string code, string message, string? detailCode = null) =>
        new(ErrorKind.PolicyDenied, code, message, detailCode);

    /// <summary>
    /// Creates a timeout error (TER-US-012 AC-3: interactive prompt not matched within window;
    /// maps to Orchestrator errorCategory=TIMEOUT).
    /// </summary>
    public static Error Timeout(string code, string message, string? detailCode = null) =>
        new(ErrorKind.Timeout, code, message, detailCode);
}

public enum ErrorKind
{
    General,
    Validation,
    Environment,

    /// <summary>Identity or credential mismatch.</summary>
    Authentication,

    /// <summary>Policy explicitly prohibits the requested operation.</summary>
    PolicyDenied,

    /// <summary>
    /// An operation did not complete within its allotted time window.
    /// Maps to Orchestrator errorCategory=TIMEOUT.
    /// </summary>
    Timeout
}
