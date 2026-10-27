#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Protocol;

namespace Tracto.Contract.Validation;

/// <summary>
/// Checks all four C4 dispatch eligibility conditions and returns a <see cref="ValidationResult"/>
/// that collects every failed condition rather than stopping at the first.
/// </summary>
public sealed class DispatchEligibilityChecker
{
    /// <summary>
    /// Evaluates all C4 dispatch eligibility conditions for the given worker and request.
    /// </summary>
    /// <param name="worker">The candidate worker registration.</param>
    /// <param name="request">Details of the job to be dispatched.</param>
    /// <param name="workerSessionHasActiveLease">
    ///     True when the worker's interactiveSessionId already holds an active foreground-input lease.
    /// </param>
    /// <returns>
    ///     <see cref="ValidationResult.Ok()"/> when all conditions pass;
    ///     otherwise a failed result that carries one diagnostic per violated condition.
    ///     Silent acceptance is prohibited: callers must reject on any non-OK result
    ///     with errorCategory=VALIDATION (C4 Rule — silent acceptance prohibited).
    /// </returns>
    public ValidationResult CheckEligibility(
        WorkerRegistration worker,
        DispatchRequest request,
        bool workerSessionHasActiveLease)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        // Condition 1: runtimeType matches the job's script runtimeType.
        if (!string.Equals(
                worker.RuntimeType.ToString(),
                request.ScriptRuntimeType,
                StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationSeverity.Error,
                "RUNTIME_TYPE_MISMATCH",
                $"Job requires runtimeType='{request.ScriptRuntimeType}' but worker has runtimeType='{worker.RuntimeType}'.",
                "runtimeType"));
        }

        // Condition 2: the job's schemaVersion major is listed in supportedSchemaMajors.
        if (request.SchemaVersionMajor.HasValue
            && (worker.SupportedSchemaMajors is null
                || !worker.SupportedSchemaMajors.Contains(request.SchemaVersionMajor.Value)))
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationSeverity.Error,
                "SCHEMA_MAJOR_NOT_SUPPORTED",
                $"Job requires schema major {request.SchemaVersionMajor} but worker supports " +
                $"[{string.Join(", ", worker.SupportedSchemaMajors ?? Array.Empty<int>())}].",
                "supportedSchemaMajors"));
        }

        // Condition 3: every action namespace required by the job is covered by the worker.
        if (request.RequiredActionNamespaces is { Count: > 0 }
            && worker.AdvertisedCapabilities?.ActionNamespaces is { } advertised)
        {
            foreach (string required in request.RequiredActionNamespaces)
            {
                bool covered = advertised.Any(a =>
                    string.Equals(a, required, StringComparison.Ordinal)
                    || required.StartsWith(a.TrimEnd('*'), StringComparison.Ordinal));

                if (!covered)
                {
                    diagnostics.Add(new ValidationDiagnostic(
                        ValidationSeverity.Error,
                        "ACTION_NAMESPACE_NOT_COVERED",
                        $"Job requires action namespace '{required}' which is not in " +
                        "worker's advertisedCapabilities.actionNamespaces.",
                        "advertisedCapabilities.actionNamespaces",
                        required));
                }
            }
        }

        // Condition 4: interactive session lease fencing (C4 Rule 5).
        // A second interactive job must not be dispatched to a session that already holds an active lease.
        if (request.IsInteractive && workerSessionHasActiveLease)
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationSeverity.Error,
                "INTERACTIVE_SESSION_ALREADY_LEASED",
                "The worker's interactiveSessionId already holds an active foreground-input lease. " +
                "A second interactive job cannot be dispatched to the same session (C4 Rule 5).",
                "interactiveSessionId"));
        }

        return diagnostics.Count == 0
            ? ValidationResult.Ok()
            : ValidationResult.Fail(diagnostics);
    }
}

/// <summary>
/// Describes the job being evaluated for dispatch eligibility.
/// </summary>
/// <param name="ScriptRuntimeType">
///     The runtimeType declared in the job script (matched case-insensitively against the worker's runtimeType).
/// </param>
/// <param name="SchemaVersionMajor">
///     Major component of the job's contract schema version, or null when the job carries no version claim.
/// </param>
/// <param name="RequiredActionNamespaces">
///     All action namespace tokens used by steps in the job; null or empty means no namespace check is performed.
/// </param>
/// <param name="IsInteractive">
///     True when the job requires a foreground-input (interactive) session.
/// </param>
public record DispatchRequest(
    string ScriptRuntimeType,
    int? SchemaVersionMajor,
    IReadOnlyList<string>? RequiredActionNamespaces,
    bool IsInteractive);
