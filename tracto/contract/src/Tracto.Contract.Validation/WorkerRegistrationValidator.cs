using System.Text.Json;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Protocol;

namespace Tracto.Contract.Validation;

/// <summary>
/// Validates WorkerRegistration messages against all C4-approved behavioural rules.
/// Silent acceptance is prohibited: all violations are surfaced as VALIDATION errors or warnings.
/// </summary>
public sealed class WorkerRegistrationValidator : IWorkerRegistrationValidator
{
    private readonly ISchemaValidator _schemaValidator;
    private readonly CapabilityDigestCalculator _digestCalculator;
    private readonly SchemaVersionCatalogue _catalogue;

    // Namespace prefix that each runtimeType is expected to appear in (C4 Rule 2).
    private static readonly IReadOnlyDictionary<RuntimeType, string> RuntimeNamespacePrefix =
        new Dictionary<RuntimeType, string>
        {
            [RuntimeType.Desktop]  = "desktop.",
            [RuntimeType.Browser]  = "browser.",
            [RuntimeType.Terminal] = "terminal.",
        };

    // Common namespaces that are permitted for all runtimes.
    private static readonly HashSet<string> CommonNamespacePrefixes =
        new(StringComparer.Ordinal) { "service.", "control.", "value." };

    public WorkerRegistrationValidator(
        ISchemaValidator schemaValidator,
        CapabilityDigestCalculator digestCalculator,
        SchemaVersionCatalogue catalogue)
    {
        _schemaValidator = schemaValidator;
        _digestCalculator = digestCalculator;
        _catalogue = catalogue;
    }

    /// <summary>
    /// Convenience factory: loads schema resources and returns a ready-to-use validator.
    /// </summary>
    public static async Task<WorkerRegistrationValidator> CreateAsync(CancellationToken ct = default)
    {
        var validator = await SchemaValidator.CreateAsync(ct);
        var catalogue = await SchemaLoader.LoadCatalogueAsync(ct);
        return new WorkerRegistrationValidator(validator, new CapabilityDigestCalculator(), catalogue);
    }

    /// <inheritdoc/>
    public async Task<ValidationResult> ValidateAsync(string registrationJson, CancellationToken ct = default)
    {
        // Step 1: JSON schema validation.
        var schemaResult = await _schemaValidator.ValidateWorkerRegistrationAsync(registrationJson, ct);
        if (!schemaResult.IsValid) return schemaResult;

        // Step 2: Deserialize then apply behavioural rules.
        WorkerRegistration? registration;
        try
        {
            registration = JsonSerializer.Deserialize<WorkerRegistration>(registrationJson);
        }
        catch (JsonException ex)
        {
            return ValidationResult.Fail(
                new ValidationDiagnostic(ValidationSeverity.Error, "DESERIALIZE_ERROR", ex.Message));
        }

        if (registration is null)
            return ValidationResult.Fail(
                new ValidationDiagnostic(ValidationSeverity.Error, "NULL_REGISTRATION",
                    "Registration deserialized to null."));

        return ValidateObject(registration);
    }

    /// <inheritdoc/>
    public ValidationResult ValidateObject(WorkerRegistration registration)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        // Rule 6: supportedSchemaMajors must include the current schema major version.
        if (registration.SupportedSchemaMajors is null ||
            !registration.SupportedSchemaMajors.Contains(_catalogue.CurrentMajor))
        {
            diagnostics.Add(new ValidationDiagnostic(
                ValidationSeverity.Error,
                "UNSUPPORTED_SCHEMA_MAJOR",
                $"Worker must include current schema major {_catalogue.CurrentMajor} in supportedSchemaMajors.",
                "supportedSchemaMajors"));
        }

        // Rule 2: runtimeType must be consistent with actionNamespaces prefixes.
        if (registration.AdvertisedCapabilities?.ActionNamespaces is { } actionNs)
            ValidateNamespaceConsistency(registration.RuntimeType, actionNs, "actionNamespaces", diagnostics);

        // Rule 4: interactiveSessionId fencing.
        ValidateInteractiveSession(registration, diagnostics);

        // Rule 3: capabilityDigest integrity check (warning only — not an authentication gate, per C4/ST1-09-AC03).
        if (registration.CapabilityDigest is not null && registration.AdvertisedCapabilities is not null)
        {
            var (isMatch, expected) = _digestCalculator.Verify(registration);
            if (!isMatch)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    ValidationSeverity.Warning,
                    "CAPABILITY_DIGEST_MISMATCH",
                    "capabilityDigest does not match the computed value. This indicates possible " +
                    "capability-map mutation. " +
                    "Note: the digest is an integrity hint only — not an authentication gate (C4, ST1-09-AC03).",
                    "capabilityDigest",
                    expected));
            }
        }

        // Warnings (e.g. CAPABILITY_DIGEST_MISMATCH) do not block registration:
        // IsValid = true even when warnings are present; only error-severity diagnostics
        // cause rejection (C4, ST1-09-AC03: digest is an integrity hint, not an auth gate).
        bool hasErrors = diagnostics.Any(d => d.Severity == ValidationSeverity.Error);
        if (diagnostics.Count == 0) return ValidationResult.Ok();
        return hasErrors
            ? ValidationResult.Fail(diagnostics)
            : new ValidationResult(true, diagnostics);
    }

    private static void ValidateNamespaceConsistency(
        RuntimeType runtimeType,
        IReadOnlyList<string> actionNamespaces,
        string path,
        List<ValidationDiagnostic> diagnostics)
    {
        // Only runtimeTypes with a known prefix are subject to this rule.
        if (!RuntimeNamespacePrefix.TryGetValue(runtimeType, out var expectedPrefix)) return;

        foreach (var ns in actionNamespaces)
        {
            // Common namespaces (service.*, control.*, value.*) are allowed for all runtimes.
            bool isCommon = CommonNamespacePrefixes.Any(p => ns.StartsWith(p, StringComparison.Ordinal));
            if (isCommon) continue;

            bool matchesOwn = ns.StartsWith(expectedPrefix, StringComparison.Ordinal);
            if (!matchesOwn)
            {
                // Reject only when the namespace belongs to a DIFFERENT runtime's well-known prefix.
                bool isOtherRuntime = RuntimeNamespacePrefix.Values
                    .Any(p => p != expectedPrefix && ns.StartsWith(p, StringComparison.Ordinal));

                if (isOtherRuntime)
                {
                    diagnostics.Add(new ValidationDiagnostic(
                        ValidationSeverity.Error,
                        "NAMESPACE_RUNTIME_MISMATCH",
                        $"Worker declares runtimeType='{runtimeType}' but actionNamespace '{ns}' " +
                        "belongs to a different runtime. " +
                        "runtimeType must be consistent with actionNamespace prefixes (C4 Rule 2).",
                        path,
                        ns));
                }
            }
        }
    }

    private static void ValidateInteractiveSession(
        WorkerRegistration registration,
        List<ValidationDiagnostic> diagnostics)
    {
        var hostClass = registration.HostClass;
        var sessionId = registration.InteractiveSessionId;

        if (hostClass == HostClass.Interactive)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                diagnostics.Add(new ValidationDiagnostic(
                    ValidationSeverity.Error,
                    "MISSING_INTERACTIVE_SESSION_ID",
                    "Workers with hostClass=interactive must supply a non-null, non-empty " +
                    "interactiveSessionId. " +
                    "interactiveSessionId identifies the foreground session, not the machine (C4 Rule 4).",
                    "interactiveSessionId"));
            }
        }
        else if (hostClass == HostClass.Headless)
        {
            if (!string.IsNullOrEmpty(sessionId))
            {
                diagnostics.Add(new ValidationDiagnostic(
                    ValidationSeverity.Error,
                    "UNEXPECTED_INTERACTIVE_SESSION_ID",
                    "Workers with hostClass=headless must not supply an interactiveSessionId.",
                    "interactiveSessionId"));
            }
        }
    }
}
