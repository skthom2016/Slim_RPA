using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Interfaces;
using Tracto.TerminalRuntime.Domain.Bindings;

namespace Tracto.TerminalRuntime.Infrastructure.Security;

/// <summary>
/// Resolves a step's <see cref="EnvironmentBinding"/> list into a concrete environment
/// dictionary for child-process injection.
///
/// Security invariants enforced here:
/// <list type="bullet">
///   <item>Only bindings explicitly declared in the step are included (AC-3).</item>
///   <item>The host ambient environment (<see cref="Environment.GetEnvironmentVariables()"/>)
///         is NEVER forwarded to the child process.</item>
///   <item>Secret values are resolved through <see cref="ISecretStore"/>. Their names are
///         collected in <see cref="EnvironmentBuildResult.SecretVariableNames"/> so that callers
///         can exclude them from audit records, evidence, and telemetry (AC-4, AC-5).</item>
/// </list>
/// </summary>
public sealed class EnvironmentBuilder(ISecretStore secretStore) : IEnvironmentBuilder
{
    /// <inheritdoc/>
    public async Task<Result<EnvironmentBuildResult, Error>> BuildAsync(
        IReadOnlyList<EnvironmentBinding> bindings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var secretNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Name))
            {
                return Result<EnvironmentBuildResult, Error>.Fail(
                    Error.Validation(
                        "terminal.invalidBinding",
                        "An environment binding has a null or empty Name."));
            }

            if (binding.SecretRef is not null)
            {
                // Resolve the secret — the value is opaque to callers outside this method.
                var secretResult = await secretStore
                    .GetSecretAsync(binding.SecretRef, ct)
                    .ConfigureAwait(false);

                if (secretResult.IsFailure)
                    return Result<EnvironmentBuildResult, Error>.Fail(secretResult.Error);

                variables[binding.Name] = secretResult.Value;
                secretNames.Add(binding.Name);  // mark so audit code knows to redact
            }
            else if (binding.Value is not null)
            {
                variables[binding.Name] = binding.Value;
            }
            else
            {
                return Result<EnvironmentBuildResult, Error>.Fail(
                    Error.Validation(
                        "terminal.invalidBinding",
                        $"Binding '{binding.Name}' has neither Value nor SecretRef set."));
            }
        }

        // Ambient environment is intentionally NOT merged (AC-3).
        return Result<EnvironmentBuildResult, Error>.Ok(
            new EnvironmentBuildResult(variables, secretNames));
    }
}
