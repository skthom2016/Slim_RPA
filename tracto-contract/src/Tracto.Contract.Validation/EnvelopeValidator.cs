using System.Text.Json;
using Tracto.Contract.Models.Envelope;

namespace Tracto.Contract.Validation;

public sealed class EnvelopeValidator : IEnvelopeValidator
{
    private readonly ISchemaValidator _schemaValidator;
    private readonly SchemaVersionCatalogue _catalogue;

    // Valid var types per the contract schema (C1 — APPROVED).
    private static readonly HashSet<string> ValidVarTypes =
        new(StringComparer.Ordinal) { "string", "integer", "boolean" };

    public EnvelopeValidator(ISchemaValidator schemaValidator, SchemaVersionCatalogue catalogue)
    {
        _schemaValidator = schemaValidator;
        _catalogue = catalogue;
    }

    public async Task<ValidationResult> ValidateAsync(string envelopeJson, CancellationToken ct = default)
    {
        // Step 1: JSON Schema validation
        var schemaResult = await _schemaValidator.ValidateEnvelopeAsync(envelopeJson, ct);
        if (!schemaResult.IsValid) return schemaResult;

        // Step 2: Deserialize and run semantic validation
        AutomationEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<AutomationEnvelope>(envelopeJson); }
        catch (JsonException ex)
        {
            return ValidationResult.Fail(
                new ValidationDiagnostic(ValidationSeverity.Error, "DESERIALIZE_ERROR", ex.Message));
        }

        if (envelope is null)
            return ValidationResult.Fail(
                new ValidationDiagnostic(ValidationSeverity.Error, "NULL_ENVELOPE", "Envelope deserialized to null."));

        return ValidateObject(envelope);
    }

    public ValidationResult ValidateObject(AutomationEnvelope envelope)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        // Rule 1: Schema version must be in catalogue
        if (!_catalogue.IsVersionSupported(envelope.SchemaVersion))
        {
            diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "UNSUPPORTED_SCHEMA_VERSION",
                $"Schema version '{envelope.SchemaVersion}' is not in the supported majors list. Republish with a supported version.",
                "schemaVersion"));
        }

        // Rule 2: automationId format — lowercase, path-like, no '..' traversal
        ValidateAutomationId(envelope.AutomationId, diagnostics);

        // Rule 3: outputs must each have a type (ST1-01-AC07)
        if (envelope.Outputs is not null)
        {
            foreach (var (name, output) in envelope.Outputs)
            {
                // evidenceClassification is required in schema, but double-check type semantics
                if (string.IsNullOrEmpty(output.Type))
                    diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "OUTPUT_MISSING_TYPE",
                        $"Output '{name}' has no type declaration.", $"outputs.{name}.type"));
            }
        }

        // Rule 4: vars validation (C1 — APPROVED)
        if (envelope.Vars is not null)
            ValidateVarDeclarations(envelope.Vars, diagnostics);

        // Rule 5: steps must not be empty (schema enforces minItems:1 but double-check)
        if (envelope.Steps is null || envelope.Steps.Count == 0)
            diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "NO_STEPS",
                "Automation must have at least one step.", "steps"));

        // Rule 6: step IDs must be unique
        if (envelope.Steps is not null)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var step in envelope.Steps)
            {
                if (!seen.Add(step.StepId))
                    diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "DUPLICATE_STEP_ID",
                        $"Step ID '{step.StepId}' is duplicated.", $"steps[{step.StepId}]"));
            }
        }

        // Rule 7: validate all var references in steps
        if (envelope.Vars is not null && envelope.Steps is not null)
            ValidateVarReferences(envelope.Vars, envelope.Steps, diagnostics);

        return diagnostics.Count == 0 ? ValidationResult.Ok() : ValidationResult.Fail(diagnostics);
    }

    private static void ValidateAutomationId(string automationId, List<ValidationDiagnostic> diagnostics)
    {
        if (automationId.Contains(".."))
            diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "INVALID_AUTOMATION_ID",
                "automationId must not contain path traversal sequences ('..').", "automationId"));

        if (automationId != automationId.ToLowerInvariant())
            diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "INVALID_AUTOMATION_ID",
                "automationId must be lowercase.", "automationId"));
    }

    private static void ValidateVarDeclarations(
        IReadOnlyDictionary<string, VarDeclaration> vars,
        List<ValidationDiagnostic> diagnostics)
    {
        foreach (var (name, decl) in vars)
        {
            if (!ValidVarTypes.Contains(decl.Type))
                diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "INVALID_VAR_TYPE",
                    $"Var '{name}' has unsupported type '{decl.Type}'. Only string, integer, boolean are allowed.",
                    $"vars.{name}.type",
                    "variable.typeMismatch")); // VariableErrorCodes.TypeMismatch
        }
    }

    private static void ValidateVarReferences(
        IReadOnlyDictionary<string, VarDeclaration> vars,
        IReadOnlyList<StepDefinition> steps,
        List<ValidationDiagnostic> diagnostics)
    {
        // Check that any outputBinding targets a declared var
        foreach (var step in steps)
        {
            if (step.OutputBinding is null) continue;
            foreach (var (varName, _) in step.OutputBinding)
            {
                if (!vars.ContainsKey(varName))
                    diagnostics.Add(new ValidationDiagnostic(ValidationSeverity.Error, "UNDECLARED_VAR_REFERENCE",
                        $"Step '{step.StepId}' outputBinding references undeclared var '{varName}'.",
                        $"steps[{step.StepId}].outputBinding.{varName}",
                        "variable.undeclaredReference")); // VariableErrorCodes.UndeclaredReference
            }
        }
    }
}
