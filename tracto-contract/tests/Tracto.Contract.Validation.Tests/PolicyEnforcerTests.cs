#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Envelope;
using Tracto.Contract.Validation;
using Xunit;

namespace Tracto.Contract.Validation.Tests;

public sealed class PolicyEnforcerTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PolicyEnforcer BuildEnforcer(params string[] registeredRefs)
    {
        var entries = registeredRefs
            .Select(r => new PolicyRegistryEntry(r, Array.Empty<string>()))
            .ToList();
        return new PolicyEnforcer(new PolicyRegistry(entries));
    }

    private static AutomationEnvelope BuildEnvelope(IReadOnlyList<string>? policyRefs = null)
        => new AutomationEnvelope(
            SchemaVersion: "1.0",
            AutomationId: "test-automation",
            RuntimeType: RuntimeType.Desktop,
            PolicyRefs: policyRefs,
            Inputs: null,
            Outputs: null,
            Vars: null,
            Steps: Array.Empty<StepDefinition>());

    // ── EnforcePolicies ───────────────────────────────────────────────────────

    [Fact]
    public void EnforcePolicies_NoPolicyRefs_Passes()
    {
        var sut = BuildEnforcer();
        var envelope = BuildEnvelope(policyRefs: null);

        var result = sut.EnforcePolicies(envelope);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void EnforcePolicies_EmptyPolicyRefs_Passes()
    {
        var sut = BuildEnforcer();
        var envelope = BuildEnvelope(policyRefs: Array.Empty<string>());

        var result = sut.EnforcePolicies(envelope);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void EnforcePolicies_RegisteredPolicyRef_Passes()
    {
        var sut = BuildEnforcer("policy:gdpr");
        var envelope = BuildEnvelope(policyRefs: new[] { "policy:gdpr" });

        var result = sut.EnforcePolicies(envelope);

        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void EnforcePolicies_UnregisteredPolicyRef_FailsWithDiagnostic()
    {
        var sut = BuildEnforcer("policy:gdpr");
        var envelope = BuildEnvelope(policyRefs: new[] { "policy:unknown" });

        var result = sut.EnforcePolicies(envelope);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().HaveCount(1);
        result.Diagnostics[0].Code.Should().Be("UNREGISTERED_POLICY_REF");
    }

    [Fact]
    public void EnforcePolicies_MultipleUnregisteredRefs_ProducesOneDiagnosticPerRef()
    {
        var sut = BuildEnforcer("policy:gdpr");
        var envelope = BuildEnvelope(policyRefs: new[] { "policy:unknown-a", "policy:unknown-b" });

        var result = sut.EnforcePolicies(envelope);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().HaveCount(2);
        result.Diagnostics.Should().AllSatisfy(d => d.Code.Should().Be("UNREGISTERED_POLICY_REF"));
    }

    // ── IsPolicyRegistered ────────────────────────────────────────────────────

    [Fact]
    public void IsPolicyRegistered_KnownRef_ReturnsTrue()
    {
        var sut = BuildEnforcer("policy:gdpr");

        sut.IsPolicyRegistered("policy:gdpr").Should().BeTrue();
    }

    [Fact]
    public void IsPolicyRegistered_UnknownRef_ReturnsFalse()
    {
        var sut = BuildEnforcer("policy:gdpr");

        sut.IsPolicyRegistered("policy:no-such-policy").Should().BeFalse();
    }
}
