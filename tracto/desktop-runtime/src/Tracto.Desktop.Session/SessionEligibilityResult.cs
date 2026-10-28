namespace Tracto.Desktop.Session;

/// <summary>The result of a session eligibility check.</summary>
/// <param name="IsEligible">True when the session is eligible for GUI input.</param>
/// <param name="State">The session state observed at check time.</param>
/// <param name="Reason">Human-readable explanation of the result.</param>
/// <param name="ErrorCategory">CEC v1.0.1 errorCategory value, or null when eligible.</param>
/// <param name="DetailCode">CEC v1.0.1 detailCode value, or null when eligible.</param>
public sealed record SessionEligibilityResult(
    bool IsEligible,
    SessionState State,
    string Reason,
    string? ErrorCategory,
    string? DetailCode);
