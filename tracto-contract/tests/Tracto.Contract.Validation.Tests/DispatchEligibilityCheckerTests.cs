#nullable enable

using FluentAssertions;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Validation;
using Xunit;

namespace Tracto.Contract.Validation.Tests;

/// <summary>
/// Tests for <see cref="DispatchEligibilityChecker"/> (C4 APPROVED).
/// No mocking; all real types.
/// </summary>
public sealed class DispatchEligibilityCheckerTests
{
    private readonly DispatchEligibilityChecker _sut = new();

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static WorkerRegistration MakeWorker(
        RuntimeType runtimeType                = RuntimeType.Desktop,
        IReadOnlyList<int>? schemaMajors       = null,
        IReadOnlyList<string>? actionNamespaces = null,
        HostClass hostClass                    = HostClass.Headless,
        string? interactiveSessionId           = null)
        => new(
            WorkerId:               "worker-dispatch-01",
            RuntimeType:            runtimeType,
            WorkerVersion:          "1.0.0",
            ProtocolVersion:        "1.0",
            SupportedSchemaMajors:  schemaMajors ?? new[] { 1 },
            CapabilityDigest:       string.Empty,
            HostClass:              hostClass,
            InteractiveSessionId:   interactiveSessionId,
            AdvertisedCapabilities: new AdvertisedCapabilities(
                ActionNamespaces:    actionNamespaces ?? new[] { "desktop.*" },
                PredicateNamespaces: new[] { "desktop.*" },
                MaxIterations:       50), // [Q9-Pending]
            Health:                 new(WorkerHealthStatus.Ready));

    private static DispatchRequest MakeRequest(
        string scriptRuntimeType              = "Desktop",
        int? schemaMajor                      = 1,
        IReadOnlyList<string>? requiredNs     = null,
        bool isInteractive                    = false)
        => new(
            ScriptRuntimeType:        scriptRuntimeType,
            SchemaVersionMajor:       schemaMajor,
            RequiredActionNamespaces: requiredNs,
            IsInteractive:            isInteractive);

    // ── TC1: Matching runtime, schema, namespaces, not interactive → eligible ─

    [Fact]
    public void FullyMatchingRequest_IsEligible()
    {
        var worker  = MakeWorker();
        var request = MakeRequest(
            scriptRuntimeType: "Desktop",
            schemaMajor:       1,
            requiredNs:        new[] { "desktop.*" },
            isInteractive:     false);

        var result = _sut.CheckEligibility(worker, request, workerSessionHasActiveLease: false);

        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
    }

    // ── TC2: runtimeType mismatch → RUNTIME_TYPE_MISMATCH ────────────────────

    [Fact]
    public void RuntimeTypeMismatch_ProducesRuntimeTypeMismatchDiagnostic()
    {
        var worker  = MakeWorker(runtimeType: RuntimeType.Desktop);
        var request = MakeRequest(scriptRuntimeType: "Browser");

        var result = _sut.CheckEligibility(worker, request, workerSessionHasActiveLease: false);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "RUNTIME_TYPE_MISMATCH");
    }

    // ── TC3: Schema major not in supportedSchemaMajors → SCHEMA_MAJOR_NOT_SUPPORTED ─

    [Fact]
    public void SchemaMajorNotSupported_ProducesSchemaMajorNotSupportedDiagnostic()
    {
        var worker  = MakeWorker(schemaMajors: new[] { 1 });
        var request = MakeRequest(schemaMajor: 2);

        var result = _sut.CheckEligibility(worker, request, workerSessionHasActiveLease: false);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "SCHEMA_MAJOR_NOT_SUPPORTED");
    }

    // ── TC4: Required action namespace not covered → ACTION_NAMESPACE_NOT_COVERED ─

    [Fact]
    public void RequiredNamespaceNotCovered_ProducesActionNamespaceNotCoveredDiagnostic()
    {
        var worker  = MakeWorker(actionNamespaces: new[] { "desktop.*" });
        var request = MakeRequest(requiredNs: new[] { "browser.navigate" });

        var result = _sut.CheckEligibility(worker, request, workerSessionHasActiveLease: false);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "ACTION_NAMESPACE_NOT_COVERED");
    }

    // ── TC5: Namespace pattern coverage via prefix match ─────────────────────

    [Fact]
    public void NamespacePatternCoverage_PrefixMatch_IsCovered()
    {
        // Worker advertises "desktop.*"; job requires "desktop.click" → covered by prefix.
        var worker  = MakeWorker(actionNamespaces: new[] { "desktop.*" });
        var request = MakeRequest(requiredNs: new[] { "desktop.click" });

        var result = _sut.CheckEligibility(worker, request, workerSessionHasActiveLease: false);

        result.IsValid.Should().BeTrue(
            because: "'desktop.*' covers any 'desktop.' prefix action namespace");
        result.Diagnostics.Should().NotContain(d => d.Code == "ACTION_NAMESPACE_NOT_COVERED");
    }

    // ── TC6: Interactive job + worker session already leased → INTERACTIVE_SESSION_ALREADY_LEASED ─

    [Fact]
    public void InteractiveJob_WithAlreadyLeasedSession_ProducesInteractiveSessionAlreadyLeasedDiagnostic()
    {
        var worker  = MakeWorker(
            hostClass:            HostClass.Interactive,
            interactiveSessionId: "session-active");
        var request = MakeRequest(isInteractive: true);

        // workerSessionHasActiveLease = true simulates an existing foreground-input lease.
        var result = _sut.CheckEligibility(worker, request, workerSessionHasActiveLease: true);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "INTERACTIVE_SESSION_ALREADY_LEASED");
    }

    // ── TC7: Interactive job + session not leased → eligible ─────────────────

    [Fact]
    public void InteractiveJob_WithNoActiveLease_IsEligible()
    {
        var worker  = MakeWorker(
            hostClass:            HostClass.Interactive,
            interactiveSessionId: "session-free");
        var request = MakeRequest(isInteractive: true);

        var result = _sut.CheckEligibility(worker, request, workerSessionHasActiveLease: false);

        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().NotContain(d => d.Code == "INTERACTIVE_SESSION_ALREADY_LEASED");
    }
}
