using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Settings;

namespace Tracto.Orchestrator.Infrastructure.Deployment;

/// <summary>
/// Resolves <see cref="DeploymentMode"/> from the <c>TRACTO_MODE</c> environment variable.
/// Returns a <see cref="Result{TValue,TError}"/> so startup code can surface a clear diagnostic
/// message without throwing.
/// </summary>
public static class DeploymentModeResolver
{
    /// <summary>Environment variable name that controls the deployment mode.</summary>
    public const string EnvVarName = "TRACTO_MODE";

    /// <summary>
    /// Parses <paramref name="rawValue"/> into a <see cref="DeploymentMode"/>.
    /// Comparison is case-insensitive.
    /// </summary>
    public static Result<DeploymentMode, Error> Resolve(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return Result<DeploymentMode, Error>.Failure(
                Error.Create(
                    "Configuration.MissingMode",
                    $"Environment variable '{EnvVarName}' is not set. " +
                    $"Set it to one of: {string.Join(", ", Enum.GetNames<DeploymentMode>())}."));
        }

        if (Enum.TryParse<DeploymentMode>(rawValue.Trim(), ignoreCase: true, out var mode))
        {
            return Result<DeploymentMode, Error>.Success(mode);
        }

        return Result<DeploymentMode, Error>.Failure(
            Error.Create(
                "Configuration.InvalidMode",
                $"'{rawValue}' is not a valid value for '{EnvVarName}'. " +
                $"Valid values are: {string.Join(", ", Enum.GetNames<DeploymentMode>())}."));
    }

    /// <summary>
    /// Convenience overload that reads <c>TRACTO_MODE</c> from the process environment directly.
    /// </summary>
    public static Result<DeploymentMode, Error> ResolveFromEnvironment() =>
        Resolve(Environment.GetEnvironmentVariable(EnvVarName));
}
