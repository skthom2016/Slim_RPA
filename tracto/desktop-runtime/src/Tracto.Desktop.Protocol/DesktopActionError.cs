#nullable enable

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Thrown when a Desktop Runtime action is rejected due to environment, policy or capability constraints.
/// The <see cref="ErrorCode"/> maps to a CEC v1.0.1 errorCategory value.
/// </summary>
public sealed class DesktopActionError : Exception
{
    /// <summary>CEC v1.0.1 errorCategory string (e.g. "ENVIRONMENT", "POLICY_DENIED").</summary>
    public string ErrorCode { get; }

    public DesktopActionError(string errorCode, string? message = null, Exception? inner = null)
        : base(message ?? errorCode, inner)
    {
        ErrorCode = errorCode;
    }
}
