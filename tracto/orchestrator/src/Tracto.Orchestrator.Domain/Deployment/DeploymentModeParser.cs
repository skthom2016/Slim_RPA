using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Settings;

namespace Tracto.Orchestrator.Domain.Deployment;

/// <summary>
/// Pure C# helper — no framework dependencies — that parses the TRACTO_MODE env var.
/// Placed in Domain so it can be used by both Application and Infrastructure without a circular reference.
/// The Infrastructure layer also exposes <c>DeploymentModeResolver</c> as a convenience wrapper.
/// </summary>
public static class DeploymentModeParser
{
    private const string EnvVarName = "TRACTO_MODE";

    /// <summary>
    /// Parses a raw env-var string into a <see cref="DeploymentMode"/>.
    /// Returns Failure with an actionable message when the value is missing or unrecognised.
    /// </summary>
    public static Result<DeploymentMode, Error> Parse(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return Result<DeploymentMode, Error>.Failure(
                Error.Create("TRACTO_MODE_MISSING",
                    $"Environment variable '{EnvVarName}' is missing or empty. " +
                    "Set it to 'Connected' or 'Isolated' before starting the Orchestrator."));

        var trimmed = rawValue.Trim();

        // Reject numeric strings — Enum.TryParse accepts "0"/"1" but only named values are valid.
        if (trimmed.Length > 0 && char.IsDigit(trimmed[0]))
            return Result<DeploymentMode, Error>.Failure(
                Error.Create("TRACTO_MODE_INVALID",
                    $"'{rawValue}' is not a valid value for '{EnvVarName}'. " +
                    $"Valid values: {string.Join(", ", Enum.GetNames<DeploymentMode>())}."));

        if (!Enum.TryParse<DeploymentMode>(trimmed, ignoreCase: true, out var mode))
            return Result<DeploymentMode, Error>.Failure(
                Error.Create("TRACTO_MODE_INVALID",
                    $"'{rawValue}' is not a valid value for '{EnvVarName}'. " +
                    $"Valid values: {string.Join(", ", Enum.GetNames<DeploymentMode>())}."));

        return Result<DeploymentMode, Error>.Success(mode);
    }
}
