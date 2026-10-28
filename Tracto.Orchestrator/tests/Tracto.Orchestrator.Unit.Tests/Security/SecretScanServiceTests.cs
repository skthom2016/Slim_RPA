#nullable enable
using FluentAssertions;
using Tracto.Orchestrator.Application.Security;

namespace Tracto.Orchestrator.Unit.Tests.Security;

/// <summary>
/// Unit tests for <see cref="SecretScanService"/> — OC-17.
/// </summary>
public sealed class SecretScanServiceTests
{
    private readonly SecretScanService _sut = new();

    [Fact]
    public void Scan_NullPayload_ReturnsEmpty()
    {
        var result = _sut.Scan(null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Scan_CleanPayload_ReturnsEmpty()
    {
        var result = _sut.Scan("Hello, world! No secrets here.");
        result.Should().BeEmpty();
    }

    [Fact]
    public void Scan_AwsAccessKey_DetectsViolation()
    {
        // Arrange — a syntactically valid AWS access key starts with AKIA followed by 16 upper-alphanum chars.
        const string payload = "token=AKIAIOSFODNN7EXAMPLE configuration=prod";

        // Act
        var violations = _sut.Scan(payload);

        // Assert
        violations.Should().ContainSingle(v => v.PatternName == "AWS_ACCESS_KEY");
    }

    [Fact]
    public void Scan_PemPrivateKey_DetectsViolation()
    {
        const string payload = "-----BEGIN RSA PRIVATE KEY-----\nMIIE...\n-----END RSA PRIVATE KEY-----";

        var violations = _sut.Scan(payload);

        violations.Should().ContainSingle(v => v.PatternName == "PEM_PRIVATE_KEY");
    }

    [Fact]
    public void Scan_PemBeginWithoutKey_DetectsViolation()
    {
        const string payload = "-----BEGIN PRIVATE KEY-----\nMIIE...\n-----END PRIVATE KEY-----";

        var violations = _sut.Scan(payload);

        violations.Should().ContainSingle(v => v.PatternName == "PEM_PRIVATE_KEY");
    }

    [Fact]
    public void Scan_PasswordAssignment_DetectsViolation()
    {
        const string payload = @"{""username"":""alice"",""password=secret123""}";

        var violations = _sut.Scan(payload);

        violations.Should().ContainSingle(v => v.PatternName == "PASSWORD_ASSIGNMENT");
    }

    [Fact]
    public void Scan_MultiplePatterns_DetectsAllViolations()
    {
        const string payload =
            "password=abc123 and key AKIAIOSFODNN7EXAMPLE and -----BEGIN CERTIFICATE-----";

        var violations = _sut.Scan(payload);

        violations.Select(v => v.PatternName)
            .Should().Contain("AWS_ACCESS_KEY")
            .And.Contain("PASSWORD_ASSIGNMENT")
            .And.Contain("PEM_CERTIFICATE");
    }

    [Fact]
    public void ScanAll_MixedPayloads_ReturnsAllViolations()
    {
        var payloads = new[]
        {
            "clean text",
            null,
            "password=hunter2",
            "AKIAIOSFODNN7EXAMPLE",
        };

        var violations = _sut.ScanAll(payloads);

        violations.Should().HaveCountGreaterThanOrEqualTo(2);
        violations.Select(v => v.PatternName)
            .Should().Contain("PASSWORD_ASSIGNMENT")
            .And.Contain("AWS_ACCESS_KEY");
    }
}
