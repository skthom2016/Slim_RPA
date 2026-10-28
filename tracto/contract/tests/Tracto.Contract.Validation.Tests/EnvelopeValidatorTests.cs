using FluentAssertions;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Envelope;
using Tracto.Contract.Testing;
using Tracto.Contract.Validation;
using Xunit;

namespace Tracto.Contract.Validation.Tests;

/// <summary>
/// Shared xUnit fixture: creates the real SchemaValidator (from embedded resources)
/// and composes an EnvelopeValidator from it.  Initialized once per collection.
/// </summary>
public sealed class SchemaTestFixture : IAsyncLifetime
{
    public SchemaValidator SchemaValidator { get; private set; } = null!;
    public SchemaVersionCatalogue Catalogue { get; private set; } = null!;
    public EnvelopeValidator EnvelopeValidator { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Catalogue = await SchemaLoader.LoadCatalogueAsync();
        SchemaValidator = await SchemaValidator.CreateAsync();
        EnvelopeValidator = new EnvelopeValidator(SchemaValidator, Catalogue);
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Semantic-validation tests for <see cref="EnvelopeValidator"/> (ST1-01 acceptance criteria).
/// All tests exercise <see cref="EnvelopeValidator.ValidateObject"/> with real implementations —
/// no mocking frameworks are used.
/// </summary>
public sealed class EnvelopeValidatorTests : IClassFixture<SchemaTestFixture>
{
    private readonly EnvelopeValidator _validator;

    public EnvelopeValidatorTests(SchemaTestFixture fixture)
    {
        _validator = fixture.EnvelopeValidator;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Builds a minimal valid envelope with a single step.</summary>
    private static AutomationEnvelope BuildValidEnvelope(
        string schemaVersion = "1.0",
        string automationId = "test/automation/hello",
        RuntimeType runtimeType = RuntimeType.Desktop)
    {
        var step = new StepDefinitionBuilder("step1", "desktop.click").Build();
        return new AutomationEnvelopeBuilder()
            .WithSchemaVersion(schemaVersion)
            .WithAutomationId(automationId)
            .WithRuntimeType(runtimeType)
            .WithStep(step)
            .Build();
    }

    // -----------------------------------------------------------------------
    // Test cases
    // -----------------------------------------------------------------------

    /// <summary>ST1-01 TC1: Valid minimal envelope must pass all validation rules.</summary>
    [Fact]
    public void ValidMinimalEnvelope_Passes()
    {
        var envelope = BuildValidEnvelope();

        var result = _validator.ValidateObject(envelope);

        result.IsValid.Should().BeTrue(because: "a well-formed envelope with a single step should be valid");
        result.Diagnostics.Should().BeEmpty();
    }

    /// <summary>ST1-01 TC2: Schema version not listed in the catalogue is rejected with a republish diagnostic.</summary>
    [Fact]
    public void UnsupportedSchemaVersion_IsRejectedWithRepublishDiagnostic()
    {
        var envelope = BuildValidEnvelope(schemaVersion: "99.0");

        var result = _validator.ValidateObject(envelope);

        result.IsValid.Should().BeFalse(because: "version 99.0 is not in the supported-majors catalogue");
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == "UNSUPPORTED_SCHEMA_VERSION" &&
            d.Message.Contains("Republish"));
    }

    /// <summary>ST1-01 TC3: automationId containing '..' path-traversal sequence is rejected.</summary>
    [Fact]
    public void AutomationId_WithPathTraversal_IsRejected()
    {
        var envelope = BuildValidEnvelope(automationId: "test/../etc/passwd");

        var result = _validator.ValidateObject(envelope);

        result.IsValid.Should().BeFalse(because: "automationId must not contain path traversal sequences");
        result.Diagnostics.Should().Contain(d => d.Code == "INVALID_AUTOMATION_ID");
    }

    /// <summary>ST1-01 TC4: automationId containing uppercase letters is rejected.</summary>
    [Fact]
    public void AutomationId_WithUppercase_IsRejected()
    {
        var envelope = BuildValidEnvelope(automationId: "Test/Automation");

        var result = _validator.ValidateObject(envelope);

        result.IsValid.Should().BeFalse(because: "automationId must be fully lowercase");
        result.Diagnostics.Should().Contain(d => d.Code == "INVALID_AUTOMATION_ID");
    }

    /// <summary>ST1-01 TC5: Two steps sharing the same stepId are rejected.</summary>
    [Fact]
    public void DuplicateStepIds_AreRejected()
    {
        var step1 = new StepDefinitionBuilder("step1", "desktop.click").Build();
        var step2 = new StepDefinitionBuilder("step1", "desktop.type").Build();

        var envelope = new AutomationEnvelopeBuilder()
            .WithSchemaVersion("1.0")
            .WithAutomationId("test/automation/hello")
            .WithRuntimeType(RuntimeType.Desktop)
            .WithStep(step1)
            .WithStep(step2)
            .Build();

        var result = _validator.ValidateObject(envelope);

        result.IsValid.Should().BeFalse(because: "duplicate stepIds are not allowed");
        result.Diagnostics.Should().ContainSingle(d => d.Code == "DUPLICATE_STEP_ID");
    }

    /// <summary>ST1-01 TC6 (C1): Valid var declarations using allowed types pass validation.</summary>
    [Fact]
    public void ValidVarDeclarations_Pass()
    {
        var envelope = new AutomationEnvelopeBuilder()
            .WithSchemaVersion("1.0")
            .WithAutomationId("test/automation/hello")
            .WithRuntimeType(RuntimeType.Desktop)
            .WithVar("x", new VarDeclaration(Type: "string"))
            .WithVar("n", new VarDeclaration(Type: "integer", Nullable: true))
            .WithStep(new StepDefinitionBuilder("step1", "desktop.click").Build())
            .Build();

        var result = _validator.ValidateObject(envelope);

        result.IsValid.Should().BeTrue(because: "string and integer are valid var types");
        result.Diagnostics.Should().BeEmpty();
    }

    /// <summary>ST1-01 TC7 (C1): A var declaration with an unsupported type is rejected.</summary>
    [Fact]
    public void InvalidVarType_IsRejected()
    {
        var envelope = new AutomationEnvelopeBuilder()
            .WithSchemaVersion("1.0")
            .WithAutomationId("test/automation/hello")
            .WithRuntimeType(RuntimeType.Desktop)
            .WithVar("x", new VarDeclaration(Type: "expression"))
            .WithStep(new StepDefinitionBuilder("step1", "desktop.click").Build())
            .Build();

        var result = _validator.ValidateObject(envelope);

        result.IsValid.Should().BeFalse(because: "'expression' is not an allowed var type");
        result.Diagnostics.Should().ContainSingle(d => d.Code == "INVALID_VAR_TYPE");
    }

    /// <summary>
    /// ST1-01 TC8 (C1): A step outputBinding referencing a var name absent from the vars map is rejected.
    /// The validator only runs var-reference checks when the vars map is present; therefore the envelope
    /// declares one var ("declaredVar") while the step binding targets a different name ("undeclaredVar").
    /// </summary>
    [Fact]
    public void OutputBinding_ReferencingUndeclaredVar_IsRejected()
    {
        // "declaredVar" exists in the vars map; the step binding targets "undeclaredVar" which does not.
        var step = new StepDefinitionBuilder("s1", "desktop.click")
            .WithOutputBinding("undeclaredVar", "output.value")
            .Build();

        var envelope = new AutomationEnvelopeBuilder()
            .WithSchemaVersion("1.0")
            .WithAutomationId("test/automation/hello")
            .WithRuntimeType(RuntimeType.Desktop)
            .WithVar("declaredVar", new VarDeclaration(Type: "string"))
            .WithStep(step)
            .Build();

        var result = _validator.ValidateObject(envelope);

        result.IsValid.Should().BeFalse(because: "outputBinding targets a var name that is not in the vars map");
        result.Diagnostics.Should().ContainSingle(d => d.Code == "UNDECLARED_VAR_REFERENCE");
    }
}
