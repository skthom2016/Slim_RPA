using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace Tracto.Contract.Validation;

public sealed class SchemaValidator : ISchemaValidator
{
    private readonly JsonSchema _envelopeSchema;
    private readonly JsonSchema _workerRegistrationSchema;

    private SchemaValidator(JsonSchema envelopeSchema, JsonSchema workerRegSchema, SchemaVersionCatalogue catalogue)
    {
        _envelopeSchema = envelopeSchema;
        _workerRegistrationSchema = workerRegSchema;
        // catalogue is loaded and validated as accessible at construction time.
        // Not stored: SchemaValidator applies structural validation only;
        // version-range enforcement lives in EnvelopeValidator.
        _ = catalogue;
    }

    public static async Task<SchemaValidator> CreateAsync(CancellationToken ct = default)
    {
        var envelope = await SchemaLoader.LoadAsync("automation-envelope.schema.json", ct);
        var workerReg = await SchemaLoader.LoadAsync("worker-registration.schema.json", ct);
        var catalogue = await SchemaLoader.LoadCatalogueAsync(ct);
        return new SchemaValidator(envelope, workerReg, catalogue);
    }

    public async Task<ValidationResult> ValidateAsync(string json, string schemaFileName, CancellationToken ct = default)
    {
        var schema = await SchemaLoader.LoadAsync(schemaFileName, ct);
        return RunValidation(json, schema);
    }

    public Task<ValidationResult> ValidateEnvelopeAsync(string json, CancellationToken ct = default)
        => Task.FromResult(RunValidation(json, _envelopeSchema));

    public Task<ValidationResult> ValidateWorkerRegistrationAsync(string json, CancellationToken ct = default)
        => Task.FromResult(RunValidation(json, _workerRegistrationSchema));

    private static ValidationResult RunValidation(string json, JsonSchema schema)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException ex)
        {
            return ValidationResult.Fail(new ValidationDiagnostic(ValidationSeverity.Error, "PARSE_ERROR", ex.Message));
        }

        var results = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (results.IsValid) return ValidationResult.Ok();

        var diagnostics = results.Details
            .Where(d => !d.IsValid && d.Errors is not null)
            .SelectMany(d => d.Errors!.Select(e =>
                new ValidationDiagnostic(ValidationSeverity.Error, "SCHEMA_VIOLATION", e.Value, d.InstanceLocation.ToString(), e.Key)))
            .ToList();
        return new ValidationResult(false, diagnostics);
    }
}
