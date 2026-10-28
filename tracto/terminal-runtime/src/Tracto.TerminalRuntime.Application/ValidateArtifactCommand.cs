using System.Text.RegularExpressions;
using Tracto.TerminalRuntime.Application.Common;
using ArtifactManifest = Tracto.TerminalRuntime.Domain.ArtifactManifest;

namespace Tracto.TerminalRuntime.Application;

// ---------------------------------------------------------------------------
// Command
// ---------------------------------------------------------------------------

/// <summary>
/// Input for validating an <see cref="ArtifactManifest"/> before any process
/// is launched.  Carries the manifest to inspect; no process-launch logic here.
/// </summary>
public sealed record ValidateArtifactCommand(ArtifactManifest Manifest);

// ---------------------------------------------------------------------------
// Result payload
// ---------------------------------------------------------------------------

/// <summary>Returned by a successful validation — the manifest is cleared for use.</summary>
public sealed record ValidatedArtifact(ArtifactManifest Manifest);

// ---------------------------------------------------------------------------
// Handler
// ---------------------------------------------------------------------------

/// <summary>
/// Validates a submitted <see cref="ArtifactManifest"/> against the five
/// acceptance criteria of TER-US-020.
///
/// <list type="number">
///   <item>Unsigned artifact → rejected; errorCategory=VALIDATION.</item>
///   <item>runtimeType != "terminal" → rejected before any shell interaction.</item>
///   <item>Mixed terminal.* + browser.* action refs → rejected.</item>
///   <item>Unsupported schema major version → rejected (only major 1 accepted).</item>
///   <item>Business-arithmetic predicates or dynamic API-supplied shell strings → rejected.</item>
/// </list>
/// </summary>
public sealed class ValidateArtifactCommandHandler
{
    // Matches common numeric comparison predicates that belong in a rules engine,
    // not in a command template (e.g. "amount > 10000", "total >= 5000").
    private static readonly Regex BusinessPredicatePattern = new(
        @"\b\w+\s*[><=!]+\s*\d+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(50));

    // Matches shell-style variable expansion sourced from API results
    // (e.g. "$(apiResult.value)", "${response.body}", "{{api.output}}").
    private static readonly Regex DynamicShellInjectionPattern = new(
        @"\$\(|\$\{|\{\{",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));

    private readonly IArtifactValidator _validator;

    public ValidateArtifactCommandHandler(IArtifactValidator validator)
    {
        _validator = validator;
    }

    public Result<ValidatedArtifact, Error> Handle(ValidateArtifactCommand command)
    {
        var manifest = command.Manifest;

        // AC-1: Signature must be present and cryptographically valid.
        if (string.IsNullOrWhiteSpace(manifest.Signature))
        {
            return Result.Fail<ValidatedArtifact>(Error.Validation(
                "ARTIFACT_UNSIGNED",
                "Artifact is missing a signature and cannot be executed."));
        }

        if (!_validator.IsSignatureValid(manifest))
        {
            return Result.Fail<ValidatedArtifact>(Error.Validation(
                "ARTIFACT_SIGNATURE_INVALID",
                "Artifact signature verification failed."));
        }

        // AC-2: RuntimeType must be "terminal" — browser artifacts must be rejected
        //       before any shell interaction.
        if (!string.Equals(manifest.RuntimeType, "terminal", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Fail<ValidatedArtifact>(Error.Validation(
                "ARTIFACT_WRONG_RUNTIME",
                $"Artifact runtimeType '{manifest.RuntimeType}' is not accepted by the Terminal worker."));
        }

        // AC-3: ActionRefs must not mix terminal.* and browser.* namespaces.
        bool hasTerminalActions = manifest.ActionRefs.Any(
            r => r.StartsWith("terminal.", StringComparison.OrdinalIgnoreCase));
        bool hasBrowserActions = manifest.ActionRefs.Any(
            r => r.StartsWith("browser.", StringComparison.OrdinalIgnoreCase));

        if (hasTerminalActions && hasBrowserActions)
        {
            return Result.Fail<ValidatedArtifact>(Error.Validation(
                "ARTIFACT_MIXED_ACTION_REFS",
                "Artifact must not reference both terminal.* and browser.* actions."));
        }

        if (hasBrowserActions)
        {
            return Result.Fail<ValidatedArtifact>(Error.Validation(
                "ARTIFACT_BROWSER_ACTION_REFS",
                "Artifact contains browser.* action references and cannot run on the Terminal worker."));
        }

        // AC-4: Only schema major version 1 is supported.
        if (manifest.SchemaMajor != 1)
        {
            return Result.Fail<ValidatedArtifact>(Error.Validation(
                "ARTIFACT_UNSUPPORTED_SCHEMA",
                $"Schema major version {manifest.SchemaMajor} is not supported; only major version 1 is accepted."));
        }

        // AC-5: No business-arithmetic predicates or dynamic API-supplied shell strings
        //       in command templates.
        foreach (var cmd in manifest.CommandRefs)
        {
            if (BusinessPredicatePattern.IsMatch(cmd))
            {
                return Result.Fail<ValidatedArtifact>(Error.Validation(
                    "ARTIFACT_BUSINESS_PREDICATE",
                    "Command template contains a business-arithmetic predicate which must be handled by a rules engine, not a shell command."));
            }

            if (DynamicShellInjectionPattern.IsMatch(cmd))
            {
                return Result.Fail<ValidatedArtifact>(Error.Validation(
                    "ARTIFACT_DYNAMIC_SHELL_INJECTION",
                    "Command template contains dynamic shell expansion from an API result, which is not permitted."));
            }
        }

        return Result.Ok<ValidatedArtifact>(new ValidatedArtifact(manifest));
    }
}
