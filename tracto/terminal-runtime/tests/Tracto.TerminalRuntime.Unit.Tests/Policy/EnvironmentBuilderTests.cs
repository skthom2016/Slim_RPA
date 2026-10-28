using FluentAssertions;
using Moq;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Application.Interfaces;
using Tracto.TerminalRuntime.Domain.Bindings;
using Tracto.TerminalRuntime.Infrastructure.Security;

namespace Tracto.TerminalRuntime.Unit.Tests.Policy;

/// <summary>
/// Unit tests for <see cref="EnvironmentBuilder"/> covering acceptance criteria AC-3, AC-4,
/// and AC-5 of TER-US-003: Working Directory Policy and Environment Variable Handling.
/// </summary>
public sealed class EnvironmentBuilderTests
{
    // ── AC-3: Only explicit bindings forwarded; ambient environment NOT included ──

    [Fact]
    public async Task BuildAsync_OnlyBindingsIncluded_AmbientNotForwarded()
    {
        // Arrange
        var secretStoreMock = new Mock<ISecretStore>();
        var sut = new EnvironmentBuilder(secretStoreMock.Object);

        var bindings = new List<EnvironmentBinding>
        {
            new("MY_VAR", Value: "hello")
        };

        // Ensure at least one ambient variable exists (there always will be on CI)
        System.Environment.GetEnvironmentVariables().Count.Should().BeGreaterThan(0);

        // Act
        var result = await sut.BuildAsync(bindings);

        // Assert — only MY_VAR present, no ambient keys leaked
        result.IsSuccess.Should().BeTrue();
        result.Value.Variables.Should().ContainKey("MY_VAR").WhoseValue.Should().Be("hello");

        // Check that none of the ambient environment variables appear in the result
        foreach (System.Collections.DictionaryEntry entry
                 in System.Environment.GetEnvironmentVariables())
        {
            var ambientKey = (string)entry.Key!;
            if (ambientKey == "MY_VAR") continue; // we set this explicitly, ignore it

            result.Value.Variables.Should().NotContainKey(ambientKey,
                because: $"ambient variable '{ambientKey}' must NOT be forwarded to the child process (AC-3)");
        }
    }

    [Fact]
    public async Task BuildAsync_EmptyBindings_ReturnsEmptyEnvironment()
    {
        var secretStoreMock = new Mock<ISecretStore>();
        var sut = new EnvironmentBuilder(secretStoreMock.Object);

        var result = await sut.BuildAsync([]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Variables.Should().BeEmpty(
            because: "no bindings means no environment variables are passed to the child");
        result.Value.SecretVariableNames.Should().BeEmpty();
    }

    // ── AC-4 / AC-5: Secret values absent from audit set ────────────────────────

    [Fact]
    public async Task BuildAsync_SecretBinding_MarkedInSecretVariableNames()
    {
        // Arrange
        const string secretRef = "my-api-key";
        const string opaqueValue = "stub:abcd1234";

        var secretStoreMock = new Mock<ISecretStore>();
        secretStoreMock
            .Setup(s => s.GetSecretAsync(secretRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string, Error>.Ok(opaqueValue));

        var sut = new EnvironmentBuilder(secretStoreMock.Object);
        var bindings = new List<EnvironmentBinding>
        {
            new("API_KEY", SecretRef: secretRef)
        };

        // Act
        var result = await sut.BuildAsync(bindings);

        // Assert — the variable is present in the environment for the child process...
        result.IsSuccess.Should().BeTrue();
        result.Value.Variables.Should().ContainKey("API_KEY");

        // ...but marked as secret so audit code knows to redact it (AC-4, AC-5)
        result.Value.SecretVariableNames.Should().Contain("API_KEY",
            because: "secret-ref bindings must be tracked in SecretVariableNames for audit redaction");
    }

    [Fact]
    public async Task BuildAsync_SecretVariable_AbsentFromAuditRecord()
    {
        // Simulates what audit-producing code should do: exclude SecretVariableNames from output.
        const string secretRef = "db-password";
        const string opaqueValue = "stub:cafebabe";

        var secretStoreMock = new Mock<ISecretStore>();
        secretStoreMock
            .Setup(s => s.GetSecretAsync(secretRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string, Error>.Ok(opaqueValue));

        var sut = new EnvironmentBuilder(secretStoreMock.Object);
        var bindings = new List<EnvironmentBinding>
        {
            new("DB_HOST", Value: "db.internal"),
            new("DB_PASSWORD", SecretRef: secretRef)
        };

        var result = await sut.BuildAsync(bindings);
        result.IsSuccess.Should().BeTrue();

        // Simulate building an audit record that excludes secret keys
        var auditRecord = result.Value.Variables
            .Where(kv => !result.Value.SecretVariableNames.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // The secret key must NOT appear in the audit record (AC-5)
        auditRecord.Should().NotContainKey("DB_PASSWORD",
            because: "secret environment variables must be absent from evidence and telemetry (AC-5)");

        // The plain-text key should appear
        auditRecord.Should().ContainKey("DB_HOST",
            because: "non-secret environment variables are safe to include in audit records");

        // Verify the raw value never leaks the secretRef string itself
        result.Value.Variables["DB_PASSWORD"].Should().NotBe(secretRef,
            because: "the resolved secret value must be opaque, not a copy of the reference name");
    }

    [Fact]
    public async Task BuildAsync_PlainBinding_NotMarkedAsSecret()
    {
        var secretStoreMock = new Mock<ISecretStore>();
        var sut = new EnvironmentBuilder(secretStoreMock.Object);
        var bindings = new List<EnvironmentBinding>
        {
            new("CONFIG_URL", Value: "https://config.internal/v1")
        };

        var result = await sut.BuildAsync(bindings);

        result.IsSuccess.Should().BeTrue();
        result.Value.SecretVariableNames.Should().NotContain("CONFIG_URL",
            because: "plain-text bindings are safe to include in audit records");
    }

    // ── Validation edge cases ────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_BindingWithNoValueOrSecretRef_ReturnsFail()
    {
        var secretStoreMock = new Mock<ISecretStore>();
        var sut = new EnvironmentBuilder(secretStoreMock.Object);
        var bindings = new List<EnvironmentBinding>
        {
            new("INCOMPLETE") // neither Value nor SecretRef
        };

        var result = await sut.BuildAsync(bindings);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
    }

    [Fact]
    public async Task BuildAsync_NullOrEmptyName_ReturnsFail()
    {
        var secretStoreMock = new Mock<ISecretStore>();
        var sut = new EnvironmentBuilder(secretStoreMock.Object);
        var bindings = new List<EnvironmentBinding>
        {
            new("   ", Value: "value") // whitespace-only name
        };

        var result = await sut.BuildAsync(bindings);

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.Validation);
    }

    [Fact]
    public async Task BuildAsync_SecretStoreFailure_PropagatesError()
    {
        var secretStoreMock = new Mock<ISecretStore>();
        secretStoreMock
            .Setup(s => s.GetSecretAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<string, Error>.Fail(
                Error.General("secret.notFound", "Secret ref not found.")));

        var sut = new EnvironmentBuilder(secretStoreMock.Object);
        var bindings = new List<EnvironmentBinding>
        {
            new("API_KEY", SecretRef: "missing-secret")
        };

        var result = await sut.BuildAsync(bindings);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("secret.notFound");
    }
}
