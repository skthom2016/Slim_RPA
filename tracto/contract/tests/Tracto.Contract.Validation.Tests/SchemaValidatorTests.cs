using FluentAssertions;
using Tracto.Contract.Validation;
using Xunit;

namespace Tracto.Contract.Validation.Tests;

/// <summary>
/// JSON-Schema structural validation tests for <see cref="SchemaValidator"/>.
/// Uses <see cref="SchemaValidator.CreateAsync"/> via a shared xUnit fixture.
/// No mocking frameworks are used.
/// </summary>
public sealed class SchemaValidatorTests : IClassFixture<SchemaTestFixture>
{
    private readonly SchemaValidator _schemaValidator;

    public SchemaValidatorTests(SchemaTestFixture fixture)
    {
        _schemaValidator = fixture.SchemaValidator;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Returns a minimal valid automation-envelope JSON string.</summary>
    private static string ValidEnvelopeJson() => """
        {
          "schemaVersion": "1.0",
          "automationId": "test/automation/hello",
          "runtimeType": "desktop",
          "steps": [
            { "stepId": "step1", "action": "desktop.click" }
          ]
        }
        """;

    /// <summary>Returns a minimal valid worker-registration JSON string.</summary>
    private static string ValidWorkerRegistrationJson() => """
        {
          "workerId": "worker-001",
          "runtimeType": "desktop",
          "workerVersion": "1.0.0",
          "protocolVersion": "1.0",
          "supportedSchemaMajors": [1],
          "capabilityDigest": "sha256:0000000000000000000000000000000000000000000000000000000000000000",
          "hostClass": "interactive",
          "interactiveSessionId": "session-abc",
          "advertisedCapabilities": {
            "actionNamespaces": ["desktop.*"],
            "predicateNamespaces": ["desktop.*"],
            "maxIterations": 100
          },
          "health": {
            "status": "READY"
          }
        }
        """;

    // -----------------------------------------------------------------------
    // Automation-envelope schema tests
    // -----------------------------------------------------------------------

    /// <summary>A structurally complete automation envelope must pass schema validation.</summary>
    [Fact]
    public async Task ValidEnvelopeJson_PassesSchemaValidation()
    {
        var result = await _schemaValidator.ValidateEnvelopeAsync(ValidEnvelopeJson());

        result.IsValid.Should().BeTrue(because: "the JSON satisfies all required fields and enum constraints");
        result.Diagnostics.Should().BeEmpty();
    }

    /// <summary>An envelope missing the required "schemaVersion" field must fail schema validation.</summary>
    [Fact]
    public async Task EnvelopeJson_MissingSchemaVersion_FailsSchemaValidation()
    {
        const string json = """
            {
              "automationId": "test/automation/hello",
              "runtimeType": "desktop",
              "steps": [
                { "stepId": "step1", "action": "desktop.click" }
              ]
            }
            """;

        var result = await _schemaValidator.ValidateEnvelopeAsync(json);

        result.IsValid.Should().BeFalse(because: "schemaVersion is a required field in the envelope schema");
        result.Diagnostics.Should().NotBeEmpty();
    }

    /// <summary>An envelope with a runtimeType value outside the allowed enum is rejected by the schema.</summary>
    [Fact]
    public async Task EnvelopeJson_InvalidRuntimeType_FailsSchemaValidation()
    {
        const string json = """
            {
              "schemaVersion": "1.0",
              "automationId": "test/automation/hello",
              "runtimeType": "cloud",
              "steps": [
                { "stepId": "step1", "action": "desktop.click" }
              ]
            }
            """;

        var result = await _schemaValidator.ValidateEnvelopeAsync(json);

        result.IsValid.Should().BeFalse(because: "'cloud' is not in the runtimeType enum [desktop, browser, terminal]");
        result.Diagnostics.Should().NotBeEmpty();
    }

    // -----------------------------------------------------------------------
    // Worker-registration schema tests
    // -----------------------------------------------------------------------

    /// <summary>A structurally complete worker-registration JSON must pass schema validation.</summary>
    [Fact]
    public async Task ValidWorkerRegistrationJson_PassesSchemaValidation()
    {
        var result = await _schemaValidator.ValidateWorkerRegistrationAsync(ValidWorkerRegistrationJson());

        result.IsValid.Should().BeTrue(because: "the JSON satisfies all required fields and pattern constraints");
        result.Diagnostics.Should().BeEmpty();
    }
}
