using Microsoft.Extensions.Options;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;

namespace Tracto.TerminalRuntime.Infrastructure;

/// <summary>
/// Validates SSH remote host requests against the deployment policy.
/// Hosts not present in the allow-list are rejected with POLICY_DENIED
/// before any network connection is attempted.
/// </summary>
public sealed class RemoteContextValidator : IRemoteContextValidator
{
    private readonly RemoteContextPolicy _policy;

    public RemoteContextValidator(IOptions<RemoteContextValidatorOptions> options)
    {
        _policy = new RemoteContextPolicy(options.Value.AllowedRemoteHosts ?? []);
    }

    public Result<string, Error> ValidateRemoteHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return Result<string, Error>.Fail(
                Error.Validation("RCV_001", "Remote host must not be empty."));

        if (!_policy.IsHostAllowed(host))
            return Result<string, Error>.Fail(
                Error.PolicyDenied(
                    "RCV_002",
                    "Remote host is not configured in deployment policy. errorCategory=POLICY_DENIED"));

        return Result<string, Error>.Ok(host);
    }
}

public sealed class RemoteContextValidatorOptions
{
    public List<string>? AllowedRemoteHosts { get; set; }
}
