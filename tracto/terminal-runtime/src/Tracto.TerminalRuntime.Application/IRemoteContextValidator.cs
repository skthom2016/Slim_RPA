using Tracto.TerminalRuntime.Application.Common;

namespace Tracto.TerminalRuntime.Application;

/// <summary>
/// Validates whether a requested remote execution context is permitted by
/// the current deployment policy. Must be called before any connection attempt.
/// </summary>
public interface IRemoteContextValidator
{
    /// <summary>
    /// Returns Success(host) when the host is allowed, or Failure with
    /// ErrorKind.PolicyDenied when it is not configured in the policy.
    /// </summary>
    Result<string, Error> ValidateRemoteHost(string host);
}
