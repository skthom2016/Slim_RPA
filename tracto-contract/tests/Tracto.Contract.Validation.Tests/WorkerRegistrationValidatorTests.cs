#nullable enable

using System.Text.Json;
using FluentAssertions;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Validation;
using Xunit;

namespace Tracto.Contract.Validation.Tests;

/// <summary>
/// Shared fixture that creates a real <see cref="WorkerRegistrationValidator"/> via
/// <see cref="WorkerRegistrationValidator.CreateAsync"/> (loads schema resources once per run).
/// </summary>
public sealed class WorkerRegistrationValidatorFixture : IAsyncLifetime
{
    public WorkerRegistrationValidator Validator { get; private set; } = null!;
    public CapabilityDigestCalculator DigestCalculator { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Validator        = await WorkerRegistrationValidator.CreateAsync();
        DigestCalculator = new CapabilityDigestCalculator();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

/// <summary>
/// Behavioural tests for <see cref="WorkerRegistrationValidator"/> (C4 APPROVED).
/// No mocking; uses a shared <see cref="WorkerRegistrationValidatorFixture"/>.
/// </summary>
public sealed class WorkerRegistrationValidatorTests : IClassFixture<WorkerRegistrationValidatorFixture>
{
    private readonly WorkerRegistrationValidator _validator;
    private readonly CapabilityDigestCalculator  _digestCalc;

    public WorkerRegistrationValidatorTests(WorkerRegistrationValidatorFixture fixture)
    {
        _validator  = fixture.Validator;
        _digestCalc = fixture.DigestCalculator;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AdvertisedCapabilities MakeCaps(
        IReadOnlyList<string>? actionNamespaces    = null,
        IReadOnlyList<string>? predicateNamespaces = null,
        int maxIterations = 50) // [Q9-Pending] upper-bound still under review
        => new(
            ActionNamespaces:    actionNamespaces    ?? new[] { "desktop.*" },
            PredicateNamespaces: predicateNamespaces ?? new[] { "desktop.*" },
            MaxIterations:       maxIterations);

    private WorkerRegistration BuildDesktopWorker(
        string  workerId             = "worker-desktop-01",
        string  workerVersion        = "1.0.0",
        HostClass hostClass          = HostClass.Headless,
        string? interactiveSessionId = null,
        IReadOnlyList<int>? supportedSchemaMajors = null,
        IReadOnlyList<string>? actionNamespaces   = null,
        string? overrideDigest                    = null)
    {
        var caps     = MakeCaps(actionNamespaces: actionNamespaces ?? new[] { "desktop.*" });
        var digest   = overrideDigest
            ?? _digestCalc.Compute(workerId, "desktop", workerVersion, caps);

        return new WorkerRegistration(
            WorkerId:               workerId,
            RuntimeType:            RuntimeType.Desktop,
            WorkerVersion:          workerVersion,
            ProtocolVersion:        "1.0",
            SupportedSchemaMajors:  supportedSchemaMajors ?? new[] { 1 },
            CapabilityDigest:       digest,
            HostClass:              hostClass,
            InteractiveSessionId:   interactiveSessionId,
            AdvertisedCapabilities: caps,
            Health:                 new(WorkerHealthStatus.Ready));
    }

    // ── TC1: Valid desktop worker registration passes (with correct digest) ───

    [Fact]
    public void ValidDesktopWorker_WithCorrectDigest_Passes()
    {
        var worker = BuildDesktopWorker();

        var result = _validator.ValidateObject(worker);

        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().BeEmpty();
    }

    // ── TC2: Missing schema major in supportedSchemaMajors fails ─────────────

    [Fact]
    public void MissingSchemaMajor_FailsWithUnsupportedSchemaMajorDiagnostic()
    {
        // SupportedSchemaMajors omits major 1 (the current catalogue major).
        var worker = BuildDesktopWorker(supportedSchemaMajors: new[] { 99 });

        var result = _validator.ValidateObject(worker);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "UNSUPPORTED_SCHEMA_MAJOR");
    }

    // ── TC3: Browser worker with desktop.* namespace fails ───────────────────

    [Fact]
    public void BrowserWorker_WithDesktopNamespace_FailsWithNamespaceRuntimeMismatch()
    {
        var caps = MakeCaps(actionNamespaces: new[] { "desktop.*" });
        var digest = _digestCalc.Compute("worker-browser-01", "browser", "1.0.0", caps);

        var worker = new WorkerRegistration(
            WorkerId:               "worker-browser-01",
            RuntimeType:            RuntimeType.Browser,
            WorkerVersion:          "1.0.0",
            ProtocolVersion:        "1.0",
            SupportedSchemaMajors:  new[] { 1 },
            CapabilityDigest:       digest,
            HostClass:              HostClass.Headless,
            InteractiveSessionId:   null,
            AdvertisedCapabilities: caps,
            Health:                 new(WorkerHealthStatus.Ready));

        var result = _validator.ValidateObject(worker);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "NAMESPACE_RUNTIME_MISMATCH");
    }

    // ── TC4: Desktop worker with desktop.* namespace passes ──────────────────

    [Fact]
    public void DesktopWorker_WithDesktopNamespace_Passes()
    {
        var worker = BuildDesktopWorker(actionNamespaces: new[] { "desktop.*" });

        var result = _validator.ValidateObject(worker);

        result.IsValid.Should().BeTrue(because: "desktop.* is the canonical namespace for desktop runtime");
        result.Diagnostics.Should().BeEmpty();
    }

    // ── TC5: hostClass=interactive with null sessionId fails ──────────────────

    [Fact]
    public void Interactive_WithNullSessionId_FailsWithMissingInteractiveSessionId()
    {
        var worker = BuildDesktopWorker(
            hostClass:          HostClass.Interactive,
            interactiveSessionId: null);

        var result = _validator.ValidateObject(worker);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "MISSING_INTERACTIVE_SESSION_ID");
    }

    // ── TC6: hostClass=interactive with non-empty sessionId passes ────────────

    [Fact]
    public void Interactive_WithNonEmptySessionId_Passes()
    {
        var worker = BuildDesktopWorker(
            workerId:             "worker-interactive-01",
            hostClass:            HostClass.Interactive,
            interactiveSessionId: "session-abc-123");

        // Recompute digest: workerId is different, no overrideDigest needed since BuildDesktopWorker
        // computes it. But the caps used for digest must match the registration object exactly.
        // Because BuildDesktopWorker computes the digest internally, just call it directly.
        var result = _validator.ValidateObject(worker);

        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().NotContain(d => d.Code == "MISSING_INTERACTIVE_SESSION_ID");
    }

    // ── TC7: hostClass=headless with sessionId set fails ──────────────────────

    [Fact]
    public void Headless_WithSessionIdSet_FailsWithUnexpectedInteractiveSessionId()
    {
        var worker = BuildDesktopWorker(
            hostClass:            HostClass.Headless,
            interactiveSessionId: "should-not-be-here");

        var result = _validator.ValidateObject(worker);

        result.IsValid.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle(d => d.Code == "UNEXPECTED_INTERACTIVE_SESSION_ID");
    }

    // ── TC8: hostClass=headless with null sessionId passes ────────────────────

    [Fact]
    public void Headless_WithNullSessionId_Passes()
    {
        var worker = BuildDesktopWorker(
            hostClass:            HostClass.Headless,
            interactiveSessionId: null);

        var result = _validator.ValidateObject(worker);

        result.IsValid.Should().BeTrue();
        result.Diagnostics.Should().NotContain(d => d.Code == "UNEXPECTED_INTERACTIVE_SESSION_ID");
    }

    // ── TC9: capabilityDigest mismatch produces WARNING — validation still passes ─

    [Fact]
    public void CapabilityDigestMismatch_ProducesWarning_ValidationStillPasses()
    {
        // A deliberately wrong digest; all other rules pass.
        var worker = BuildDesktopWorker(
            overrideDigest: "sha256:0000000000000000000000000000000000000000000000000000000000000000");

        var result = _validator.ValidateObject(worker);

        // Digest mismatch is a warning, not an error — validation as a whole still passes (C4, ST1-09-AC03).
        result.IsValid.Should().BeTrue(
            because: "a capabilityDigest mismatch is an integrity hint warning, not a rejection gate");
        result.Diagnostics.Should().ContainSingle(d =>
            d.Code == "CAPABILITY_DIGEST_MISMATCH" && d.Severity == ValidationSeverity.Warning);
    }

    // ── TC10: capabilityDigest match produces no warning ─────────────────────

    [Fact]
    public void CapabilityDigestMatch_ProducesNoDigestWarning()
    {
        // BuildDesktopWorker computes the correct digest by default.
        var worker = BuildDesktopWorker();

        var result = _validator.ValidateObject(worker);

        result.Diagnostics.Should().NotContain(d => d.Code == "CAPABILITY_DIGEST_MISMATCH");
    }
}
