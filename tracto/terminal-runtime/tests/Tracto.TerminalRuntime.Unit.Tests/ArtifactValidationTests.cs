using System.Security.Cryptography;
using FluentAssertions;
using Moq;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Infrastructure;
using ArtifactManifest = Tracto.TerminalRuntime.Domain.ArtifactManifest;

namespace Tracto.TerminalRuntime.Unit.Tests;

/// <summary>
/// Unit tests for TER-US-020: Signed Artifact Validation and Closed Behavior.
///
/// Each region maps to one Acceptance Criterion.
/// </summary>
public sealed class ArtifactValidationTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static readonly byte[] TestKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Builds a minimal valid manifest whose signature is computed with <see cref="TestKey"/>.
    /// All fields satisfy every AC by default — individual tests override what they need.
    /// </summary>
    private static ArtifactManifest ValidManifest(
        string? runtimeType = "terminal",
        int schemaMajor = 1,
        string[]? actionRefs = null,
        string[]? commandRefs = null,
        string? overrideSignature = null)
    {
        var unsignedManifest = new ArtifactManifest(
            RuntimeType: runtimeType ?? "terminal",
            SchemaMajor: schemaMajor,
            ActionRefs: actionRefs ?? ["terminal.run-script"],
            CommandRefs: commandRefs ?? ["echo hello"],
            Signature: string.Empty);          // placeholder

        var sig = overrideSignature
            ?? ArtifactValidator.ComputeSignature(unsignedManifest, TestKey);

        return unsignedManifest with { Signature = sig };
    }

    /// <summary>Creates a handler backed by a real validator using <see cref="TestKey"/>.</summary>
    private static (ValidateArtifactCommandHandler Handler, ArtifactValidator Validator)
        BuildRealHandler()
    {
        var settings = Microsoft.Extensions.Options.Options.Create(new ArtifactSignatureSettings
        {
            HmacKeyBase64 = Convert.ToBase64String(TestKey)
        });

        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<ArtifactValidator>();
        var validator = new ArtifactValidator(settings, logger);
        var handler = new ValidateArtifactCommandHandler(validator);

        return (handler, validator);
    }

    // -----------------------------------------------------------------------
    // AC-1: Unsigned artifact → rejected; errorCategory=VALIDATION
    // -----------------------------------------------------------------------

    [Fact]
    public void AC1_MissingSignature_ReturnsValidationError()
    {
        var (handler, _) = BuildRealHandler();

        var manifest = new ArtifactManifest("terminal", 1, ["terminal.run-script"], ["echo hi"], Signature: "");
        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("ARTIFACT_UNSIGNED");
    }

    [Fact]
    public void AC1_WhitespaceOnlySignature_ReturnsValidationError()
    {
        var (handler, _) = BuildRealHandler();

        var manifest = new ArtifactManifest("terminal", 1, ["terminal.run-script"], ["echo hi"], Signature: "   ");
        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("ARTIFACT_UNSIGNED");
    }

    [Fact]
    public void AC1_InvalidSignature_ReturnsSignatureError()
    {
        var (handler, _) = BuildRealHandler();

        // Has a non-empty but wrong signature
        var manifest = new ArtifactManifest("terminal", 1, ["terminal.run-script"], ["echo hi"], Signature: "deadbeef");
        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("ARTIFACT_SIGNATURE_INVALID");
    }

    [Fact]
    public void AC1_ValidSignature_Passes()
    {
        var (handler, _) = BuildRealHandler();
        var manifest = ValidManifest();

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsSuccess.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // AC-2: runtimeType == "browser" → rejected before any shell interaction
    // -----------------------------------------------------------------------

    [Fact]
    public void AC2_BrowserRuntimeType_ReturnsValidationError()
    {
        var (handler, _) = BuildRealHandler();

        // Signature is computed for a "browser" manifest — so the HMAC check passes;
        // the runtime-type check must fire after that.
        var manifest = ValidManifest(
            runtimeType: "browser",
            actionRefs: ["browser.navigate"]);

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("ARTIFACT_WRONG_RUNTIME");
    }

    [Fact]
    public void AC2_UnknownRuntimeType_ReturnsValidationError()
    {
        var (handler, _) = BuildRealHandler();
        var manifest = ValidManifest(runtimeType: "desktop");

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ARTIFACT_WRONG_RUNTIME");
    }

    [Fact]
    public void AC2_TerminalRuntimeType_CaseInsensitive_Passes()
    {
        var (handler, _) = BuildRealHandler();
        var manifest = ValidManifest(runtimeType: "TERMINAL");

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        // Should pass the runtime check — may still pass overall
        result.IsSuccess.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // AC-3: Mixed terminal.* + browser.* action refs → rejected at schema validation
    // -----------------------------------------------------------------------

    [Fact]
    public void AC3_MixedActionRefs_ReturnsValidationError()
    {
        var (handler, _) = BuildRealHandler();
        var manifest = ValidManifest(
            actionRefs: ["terminal.run-script", "browser.navigate"]);

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("ARTIFACT_MIXED_ACTION_REFS");
    }

    [Fact]
    public void AC3_BrowserOnlyActionRefs_ReturnsValidationError()
    {
        var (handler, _) = BuildRealHandler();
        var manifest = ValidManifest(
            actionRefs: ["browser.navigate", "browser.click"]);

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().BeOneOf("ARTIFACT_BROWSER_ACTION_REFS", "ARTIFACT_WRONG_RUNTIME");
    }

    [Fact]
    public void AC3_TerminalOnlyActionRefs_Passes()
    {
        var (handler, _) = BuildRealHandler();
        var manifest = ValidManifest(
            actionRefs: ["terminal.run-script", "terminal.read-file"]);

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsSuccess.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // AC-4: Unsupported schema major version → rejected (only major 1 accepted)
    // -----------------------------------------------------------------------

    [Fact]
    public void AC4_SchemaMajorV2_ReturnsValidationError()
    {
        var (handler, _) = BuildRealHandler();
        var manifest = ValidManifest(schemaMajor: 2);

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("ARTIFACT_UNSUPPORTED_SCHEMA");
    }

    [Fact]
    public void AC4_SchemaMajorV0_ReturnsValidationError()
    {
        var (handler, _) = BuildRealHandler();
        var manifest = ValidManifest(schemaMajor: 0);

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ARTIFACT_UNSUPPORTED_SCHEMA");
    }

    [Fact]
    public void AC4_SchemaMajorV1_Passes()
    {
        var (handler, _) = BuildRealHandler();
        var manifest = ValidManifest(schemaMajor: 1);

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsSuccess.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // AC-5: Business predicates + dynamic shell from API results → rejected
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("if amount > 10000 then exit 1")]
    [InlineData("test total >= 5000")]
    [InlineData("[ count != 0 ]")]
    [InlineData("score < 100 && echo pass")]
    public void AC5_BusinessArithmeticPredicate_ReturnsValidationError(string command)
    {
        var (handler, _) = BuildRealHandler();
        var manifest = ValidManifest(commandRefs: [command]);

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("ARTIFACT_BUSINESS_PREDICATE");
    }

    [Theory]
    [InlineData("echo $(apiResult.value)")]
    [InlineData("curl ${response.endpoint}")]
    [InlineData("run {{api.output}}")]
    public void AC5_DynamicApiShellInjection_ReturnsValidationError(string command)
    {
        var (handler, _) = BuildRealHandler();
        var manifest = ValidManifest(commandRefs: [command]);

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Code.Should().Be("ARTIFACT_DYNAMIC_SHELL_INJECTION");
    }

    [Theory]
    [InlineData("echo hello world")]
    [InlineData("dir /s")]
    [InlineData("ping -c 4 localhost")]
    [InlineData("Get-Date -Format 'yyyy-MM-dd'")]
    public void AC5_SafeCommands_Pass(string command)
    {
        var (handler, _) = BuildRealHandler();
        var manifest = ValidManifest(commandRefs: [command]);

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsSuccess.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Fully valid artifact end-to-end
    // -----------------------------------------------------------------------

    [Fact]
    public void FullyValidArtifact_ReturnsValidatedArtifact()
    {
        var (handler, _) = BuildRealHandler();

        var manifest = ValidManifest(
            runtimeType: "terminal",
            schemaMajor: 1,
            actionRefs: ["terminal.run-script", "terminal.read-output"],
            commandRefs: ["echo start", "powershell -NonInteractive -File deploy.ps1"]);

        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsSuccess.Should().BeTrue();
        result.Value.Manifest.Should().Be(manifest);
    }

    // -----------------------------------------------------------------------
    // IArtifactValidator mocked — verifies handler does not call process launch
    // -----------------------------------------------------------------------

    [Fact]
    public void Handler_WhenValidatorReturnsFalse_ReturnsSignatureError_WithoutLaunchingProcess()
    {
        var mockValidator = new Mock<IArtifactValidator>(MockBehavior.Strict);
        mockValidator
            .Setup(v => v.IsSignatureValid(It.IsAny<ArtifactManifest>()))
            .Returns(false);

        var handler = new ValidateArtifactCommandHandler(mockValidator.Object);

        var manifest = new ArtifactManifest("terminal", 1, ["terminal.run-script"], ["echo hi"], Signature: "badhash");
        var result = handler.Handle(new ValidateArtifactCommand(manifest));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("ARTIFACT_SIGNATURE_INVALID");

        // Strict mock: only IsSignatureValid was called — no process-launch side effects
        mockValidator.Verify(v => v.IsSignatureValid(It.IsAny<ArtifactManifest>()), Times.Once);
    }
}
