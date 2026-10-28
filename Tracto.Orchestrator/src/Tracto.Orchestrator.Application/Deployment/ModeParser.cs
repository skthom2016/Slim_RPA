using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Settings;

namespace Tracto.Orchestrator.Application.Deployment;

/// <summary>
/// Parses the TRACTO_MODE environment variable into a <see cref="DeploymentMode"/> value.
/// Returns a <see cref="Result{T,TError}"/> — never throws.
/// </summary>
public static class ModeParser
{
    /// <summary>Valid environment variable name.</summary>
    public const string EnvVarName = "TRACTO_MODE";

    /// <summary>
    /// Parses <paramref name="rawValue"/> (the content of TRACTO_MODE) into a <see cref="DeploymentMode"/>.
    /// Case-insensitive. Returns a failure <see cref="Error"/> on an unrecognised or missing value.
    /// </summary>
    public static Result<DeploymentMode, Error> Parse(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return Result<DeploymentMode, Error>.Failure(
                Error.Create(
                    "TRACTO_MODE_MISSING",
                    $"Environment variable '{EnvVarName}' is not set. " +
                    "Valid values: Connected, Isolated."));
        }

        var trimmed = rawValue.Trim();

        // Reject numeric strings — Enum.TryParse accepts "0"/"1" but only named values are valid.
        if (trimmed.Length > 0 && char.IsDigit(trimmed[0]))
            return Result<DeploymentMode, Error>.Failure(
                Error.Create(
                    "TRACTO_MODE_INVALID",
                    $"Environment variable '{EnvVarName}' has invalid value '{rawValue}'. " +
                    $"Valid values: {string.Join(", ", Enum.GetNames<DeploymentMode>())}."));

        if (Enum.TryParse<DeploymentMode>(trimmed, ignoreCase: true, out var mode))
            return Result<DeploymentMode, Error>.Success(mode);

        return Result<DeploymentMode, Error>.Failure(
            Error.Create(
                "TRACTO_MODE_INVALID",
                $"Environment variable '{EnvVarName}' has invalid value '{rawValue}'. " +
                $"Valid values: {string.Join(", ", Enum.GetNames<DeploymentMode>())}."));
    }
}
