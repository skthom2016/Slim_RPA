#nullable enable

using FluentAssertions;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Validation;
using Xunit;

namespace Tracto.Contract.Validation.Tests;

/// <summary>
/// Unit tests for <see cref="CapabilityDigestCalculator"/> (C4 APPROVED).
/// The digest is an integrity hint only — not an authentication gate (ST1-09-AC03).
/// No mocking; exercises real SHA-256 computation.
/// </summary>
public sealed class CapabilityDigestCalculatorTests
{
    private readonly CapabilityDigestCalculator _sut = new();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AdvertisedCapabilities DefaultCapabilities(
        IReadOnlyList<string>? actionNamespaces = null,
        IReadOnlyList<string>? predicateNamespaces = null,
        int maxIterations = 50)
        => new(
            ActionNamespaces:    actionNamespaces    ?? new[] { "desktop.*" },
            PredicateNamespaces: predicateNamespaces ?? new[] { "desktop.*" },
            MaxIterations:       maxIterations);

    private static WorkerRegistration BuildRegistration(
        string workerId             = "worker-test-01",
        RuntimeType runtimeType     = RuntimeType.Desktop,
        string workerVersion        = "1.0.0",
        AdvertisedCapabilities? cap = null,
        string? capabilityDigest    = null)
    {
        var capabilities = cap ?? DefaultCapabilities();
        return new WorkerRegistration(
            WorkerId:               workerId,
            RuntimeType:            runtimeType,
            WorkerVersion:          workerVersion,
            ProtocolVersion:        "1.0",
            SupportedSchemaMajors:  new[] { 1 },
            CapabilityDigest:       capabilityDigest ?? string.Empty,
            HostClass:              HostClass.Headless,
            InteractiveSessionId:   null,
            AdvertisedCapabilities: capabilities,
            Health:                 new(WorkerHealthStatus.Ready));
    }

    // ── TC1: Compute returns a string starting with "sha256:" ────────────────

    [Fact]
    public void Compute_ReturnsStringStartingWithSha256Prefix()
    {
        var cap = DefaultCapabilities();
        var digest = _sut.Compute("worker-001", "desktop", "1.0.0", cap);

        digest.Should().StartWith("sha256:");
    }

    // ── TC2: Compute is deterministic ─────────────────────────────────────────

    [Fact]
    public void Compute_IsDeterministic_SameInputsProduceSameDigest()
    {
        var cap = DefaultCapabilities();

        var first  = _sut.Compute("worker-001", "desktop", "1.0.0", cap);
        var second = _sut.Compute("worker-001", "desktop", "1.0.0", cap);

        first.Should().Be(second);
    }

    // ── TC3: Compute changes when workerId changes ────────────────────────────

    [Fact]
    public void Compute_ChangesWhenWorkerIdChanges()
    {
        var cap = DefaultCapabilities();

        var digest1 = _sut.Compute("worker-A", "desktop", "1.0.0", cap);
        var digest2 = _sut.Compute("worker-B", "desktop", "1.0.0", cap);

        digest1.Should().NotBe(digest2);
    }

    // ── TC4: Compute changes when actionNamespaces change ────────────────────

    [Fact]
    public void Compute_ChangesWhenActionNamespacesChange()
    {
        var cap1 = DefaultCapabilities(actionNamespaces: new[] { "desktop.*" });
        var cap2 = DefaultCapabilities(actionNamespaces: new[] { "browser.*" });

        var digest1 = _sut.Compute("worker-001", "desktop", "1.0.0", cap1);
        var digest2 = _sut.Compute("worker-001", "desktop", "1.0.0", cap2);

        digest1.Should().NotBe(digest2);
    }

    // ── TC5: Compute is order-independent (sorted canonicalization) ───────────

    [Fact]
    public void Compute_IsOrderIndependentForNamespaces_SortedCanonicalisation()
    {
        // ["desktop.*","control.*"] and ["control.*","desktop.*"] must produce the SAME digest.
        var cap1 = DefaultCapabilities(actionNamespaces: new[] { "desktop.*", "control.*" });
        var cap2 = DefaultCapabilities(actionNamespaces: new[] { "control.*", "desktop.*" });

        var digest1 = _sut.Compute("worker-001", "desktop", "1.0.0", cap1);
        var digest2 = _sut.Compute("worker-001", "desktop", "1.0.0", cap2);

        digest1.Should().Be(digest2,
            because: "actionNamespaces are sorted alphabetically before hashing (canonical form)");
    }

    // ── TC6: Verify returns (true, expected) when digest matches ──────────────

    [Fact]
    public void Verify_ReturnsTrue_WhenDigestMatches()
    {
        var cap    = DefaultCapabilities();
        var expected = _sut.Compute("worker-001", "desktop", "1.0.0", cap);

        var registration = BuildRegistration(
            workerId:        "worker-001",
            runtimeType:     RuntimeType.Desktop,
            workerVersion:   "1.0.0",
            cap:             cap,
            capabilityDigest: expected);

        var (isMatch, computedExpected) = _sut.Verify(registration);

        isMatch.Should().BeTrue();
        computedExpected.Should().Be(expected);
    }

    // ── TC7: Verify returns (false, expected) when digest is wrong (tampered) ─

    [Fact]
    public void Verify_ReturnsFalse_WhenDigestIsTampered()
    {
        var cap = DefaultCapabilities();
        var registration = BuildRegistration(
            cap:             cap,
            capabilityDigest: "sha256:0000000000000000000000000000000000000000000000000000000000000000");

        var (isMatch, expected) = _sut.Verify(registration);

        isMatch.Should().BeFalse(because: "the stored digest does not match the computed value");
        expected.Should().StartWith("sha256:");
        expected.Should().NotBe("sha256:0000000000000000000000000000000000000000000000000000000000000000");
    }
}
