using FluentAssertions;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Infrastructure.OutputBindings;

namespace Tracto.TerminalRuntime.Unit.Tests.OutputBindings;

/// <summary>
/// Unit tests for <see cref="EnvInspectAction"/> covering AC-5 of
/// TER-US-014: Output Bindings and Approved Result Artifacts.
/// </summary>
public sealed class EnvInspectActionTests
{
    private readonly EnvInspectAction _sut = new();

    // ── AC-5: allowlisted name → returns value ────────────────────────────────────

    [Fact]
    public void Inspect_AllowlistedName_ReturnsValue()
    {
        // Arrange — set a known env var and allowlist it.
        const string varName = "TRACTO_TEST_INSPECT_OK";
        const string varValue = "hello-from-test";
        System.Environment.SetEnvironmentVariable(varName, varValue);

        try
        {
            var allowlist = new HashSet<string> { varName };
            var secrets = new HashSet<string>(); // no secrets active

            // Act
            var result = _sut.Inspect(varName, allowlist, secrets);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.Name.Should().Be(varName);
            result.Value.Value.Should().Be(varValue);
            result.Value.WasRedacted.Should().BeFalse();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }

    // ── AC-5: name not in allowlist → POLICY_DENIED ───────────────────────────────

    [Fact]
    public void Inspect_NameNotInAllowlist_ReturnsPolicyDenied()
    {
        // Arrange — "PATH" is a real env var but not allowlisted.
        var allowlist = new HashSet<string> { "ONLY_THIS_VAR" };
        var secrets = new HashSet<string>();

        // Act
        var result = _sut.Inspect("PATH", allowlist, secrets);

        // Assert
        result.IsFailure.Should().BeTrue(
            because: "'PATH' is not in the allowlist");
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied,
            because: "AC-5 mandates errorCategory=POLICY_DENIED");
        result.Error.DetailCode.Should().Be("terminal.envDenied");
    }

    // ── AC-5: secret env value → automatically redacted ──────────────────────────

    [Fact]
    public void Inspect_SecretEnvValue_IsRedacted()
    {
        // Arrange — set env var whose value matches an active secretRef value.
        const string varName = "TRACTO_TEST_INSPECT_SECRET";
        const string secretValue = "s3cr3t-v@lue-xyz";
        System.Environment.SetEnvironmentVariable(varName, secretValue);

        try
        {
            var allowlist = new HashSet<string> { varName };
            var secrets = new HashSet<string> { secretValue }; // secretRef resolved to this value

            // Act
            var result = _sut.Inspect(varName, allowlist, secrets);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.WasRedacted.Should().BeTrue(
                because: "the resolved value matched an active secretRef");
            result.Value.Value.Should().Be("[REDACTED]",
                because: "AC-5 requires automatic redaction from evidence");
            result.Value.Value.Should().NotContain(secretValue,
                because: "the plaintext secret must never appear in the result");
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void Inspect_NonSecretAllowlistedValue_IsNotRedacted()
    {
        // Arrange — env var value is NOT in the active secrets set.
        const string varName = "TRACTO_TEST_INSPECT_PLAIN";
        const string plainValue = "just-a-plain-value";
        System.Environment.SetEnvironmentVariable(varName, plainValue);

        try
        {
            var allowlist = new HashSet<string> { varName };
            var secrets = new HashSet<string> { "some-other-secret" };

            // Act
            var result = _sut.Inspect(varName, allowlist, secrets);

            // Assert
            result.IsSuccess.Should().BeTrue();
            result.Value.WasRedacted.Should().BeFalse();
            result.Value.Value.Should().Be(plainValue);
        }
        finally
        {
            System.Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public void Inspect_EmptyAllowlist_ReturnsPolicyDenied()
    {
        // Arrange — no names are allowlisted at all.
        var allowlist = new HashSet<string>();
        var secrets = new HashSet<string>();

        // Act
        var result = _sut.Inspect("ANY_NAME", allowlist, secrets);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.PolicyDenied);
    }
}
