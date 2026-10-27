namespace Tracto.Desktop.Element;

/// <summary>
/// Thrown when an operation violates a hard architectural policy.
/// For example, attempting to derive absolute pointer coordinates from a
/// Java Access Bridge anchor alone violates the coordinate-input boundary
/// mandated by DT-06-AC04 and the UIA-first coordinate constraint.
/// </summary>
public sealed class PolicyViolationException : Exception
{
    public PolicyViolationException(string policyCode)
        : base($"Policy violation: {policyCode}")
    {
        PolicyCode = policyCode;
    }

    public PolicyViolationException(string policyCode, string detail)
        : base($"Policy violation: {policyCode} — {detail}")
    {
        PolicyCode = policyCode;
    }

    public PolicyViolationException(string policyCode, string detail, Exception inner)
        : base($"Policy violation: {policyCode} — {detail}", inner)
    {
        PolicyCode = policyCode;
    }

    /// <summary>
    /// Machine-readable policy code identifying the violated constraint.
    /// Example: "JAB anchor insufficient for coordinate input"
    /// </summary>
    public string PolicyCode { get; }
}
