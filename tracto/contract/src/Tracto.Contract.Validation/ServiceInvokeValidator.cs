#nullable enable

using System.Text.Json;

namespace Tracto.Contract.Validation;

/// <summary>
/// Validates a service-invoke JSON document against the JSON schema
/// and the approved <see cref="ServiceRegistry"/>.
/// Silent acceptance is prohibited: all violations produce errorCategory=VALIDATION diagnostics.
/// </summary>
public sealed class ServiceInvokeValidator
{
    private readonly ServiceRegistry _registry;
    private readonly ISchemaValidator _schemaValidator;

    public ServiceInvokeValidator(ServiceRegistry registry, ISchemaValidator schemaValidator)
    {
        _registry = registry;
        _schemaValidator = schemaValidator;
    }

    /// <summary>
    /// Validates a service-invoke JSON document against schema + service registry.
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(string serviceInvokeJson, CancellationToken ct = default)
    {
        // Step 1: JSON schema structural validation.
        var schemaResult = await _schemaValidator.ValidateAsync(serviceInvokeJson, "service-invoke.schema.json", ct);
        if (!schemaResult.IsValid) return schemaResult;

        // Step 2: Parse for behavioural rule evaluation.
        JsonElement doc;
        try { doc = JsonDocument.Parse(serviceInvokeJson).RootElement; }
        catch (JsonException ex)
        {
            return ValidationResult.Fail(new ValidationDiagnostic(ValidationSeverity.Error, "PARSE_ERROR", ex.Message));
        }

        var diagnostics = new List<ValidationDiagnostic>();

        // Rule: serviceRef must be present and a string (schema already enforces required, but guard here for clarity).
        if (!doc.TryGetProperty("serviceRef", out var serviceRef) || serviceRef.ValueKind != JsonValueKind.String)
        {
            diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "MISSING_SERVICE_REF", "serviceRef is required."));
            return ValidationResult.Fail(diagnostics);
        }

        if (!doc.TryGetProperty("operationRef", out var operationRef) || operationRef.ValueKind != JsonValueKind.String)
        {
            diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "MISSING_OPERATION_REF", "operationRef is required."));
            return ValidationResult.Fail(diagnostics);
        }

        var sRef = serviceRef.GetString()!;
        var oRef = operationRef.GetString()!;
        var entry = _registry.Find(sRef, oRef);

        // Rule: service/operation pair must appear in the approved registry.
        if (entry is null)
        {
            diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "UNREGISTERED_SERVICE_OPERATION",
                $"Service '{sRef}' operation '{oRef}' is not in the approved service registry. " +
                $"Registered operations: [{string.Join(", ", _registry.GetRegisteredKeys())}].",
                "serviceRef", $"{sRef}/{oRef}"));
            return ValidationResult.Fail(diagnostics);
        }

        // Rule: retryClass, when present, must be in the allowed set for this operation.
        if (doc.TryGetProperty("retryClass", out var retryClass) && retryClass.ValueKind == JsonValueKind.String
            && entry.AllowedRetryClasses is { Count: > 0 })
        {
            var rc = retryClass.GetString()!;
            if (!entry.AllowedRetryClasses.Contains(rc, StringComparer.Ordinal))
                diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "DISALLOWED_RETRY_CLASS",
                    $"retryClass '{rc}' is not in the allowed set for operation '{sRef}/{oRef}'. " +
                    $"Allowed: [{string.Join(", ", entry.AllowedRetryClasses)}].",
                    "retryClass", rc));
        }

        return diagnostics.Count == 0 ? ValidationResult.Ok() : ValidationResult.Fail(diagnostics);
    }
}
