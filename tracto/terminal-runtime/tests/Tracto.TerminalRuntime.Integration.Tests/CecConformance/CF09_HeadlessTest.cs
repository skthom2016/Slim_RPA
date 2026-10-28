using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Tracto.TerminalRuntime.Domain;
using Tracto.TerminalRuntime.Infrastructure;

namespace Tracto.TerminalRuntime.Integration.Tests.CecConformance;

/// <summary>
/// CF-09 — Headless Mode Certification
///
/// CEC v1.0.1 AC-5: the Terminal worker operates in headless mode only.
///
/// This means:
///   - The WorkerCapabilityAdvertisement must have HeadlessOnly = true.
///   - The WorkerCapabilityAdvertisement must have InteractiveSessionId = null.
///   - The worker must not request or advertise a foreground input lease.
///
/// This is a dedicated certification gate separate from CF-01 to make the
/// headless invariant explicitly auditable in the conformance evidence table.
/// </summary>
public sealed class CF09_HeadlessTest : IClassFixture<MockOrchestratorFixture>
{
    private readonly MockOrchestratorFixture _fx;

    public CF09_HeadlessTest(MockOrchestratorFixture fx) => _fx = fx;

    [Fact(DisplayName = "CF-09-A: HeadlessOnly is always true in the advertisement")]
    public void Advertisement_HeadlessOnly_IsTrue()
    {
        var ad = _fx.CapabilityAdvertiser.BuildAdvertisement().Value;
        ad.HeadlessOnly.Should().BeTrue(
            "CEC AC-5: the Terminal worker must never require an interactive Windows session");
    }

    [Fact(DisplayName = "CF-09-B: InteractiveSessionId is always null in the advertisement")]
    public void Advertisement_InteractiveSessionId_IsNull()
    {
        var ad = _fx.CapabilityAdvertiser.BuildAdvertisement().Value;
        ad.InteractiveSessionId.Should().BeNull(
            "CEC AC-5: no foreground desktop input lease may be held by a Terminal worker");
    }

    [Fact(DisplayName = "CF-09-C: CapabilityAdvertiser always enforces HeadlessOnly invariant")]
    public void CapabilityAdvertiser_EnforcesHeadlessInvariant_RegardlessOfOptions()
    {
        // Even if a misconfigured options object somehow carried interactiveSessionId,
        // the CapabilityAdvertiser constructor must enforce the invariant internally.
        var options = Options.Create(new CapabilityAdvertiserOptions
        {
            WorkerId        = "worker-cf09c",
            SupportedShells = ["cmd"],
            WorkerVersion   = "0.9.0"
        });
        var clockMock = new Mock<IClock>();
        clockMock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);

        var advertiser = new CapabilityAdvertiser(options, clockMock.Object);
        var ad         = advertiser.BuildAdvertisement().Value;

        ad.HeadlessOnly.Should().BeTrue(
            "the CapabilityAdvertiser must hard-code HeadlessOnly = true regardless of options");
        ad.InteractiveSessionId.Should().BeNull(
            "the CapabilityAdvertiser must hard-code InteractiveSessionId = null regardless of options");
    }

    [Fact(DisplayName = "CF-09-D: WorkerCapabilityAdvertisement record enforces correct TerminalRuntimeType constant")]
    public void WorkerCapabilityAdvertisement_TerminalRuntimeTypeConstant_IsCorrect()
    {
        WorkerCapabilityAdvertisement.TerminalRuntimeType.Should().Be(
            "terminal",
            "the domain constant must match the CEC-specified runtime type string");
    }

    [Fact(DisplayName = "CF-09-E: Headless mode does not require foreground process (AC-5)")]
    public void HeadlessMode_DoesNotRequireForegroundProcess()
    {
        // Structural assertion: since HeadlessOnly = true and InteractiveSessionId = null,
        // no foreground desktop session is implicitly required.
        // This test codifies the mode gate as an explicit conformance entry.
        var ad = _fx.CapabilityAdvertiser.BuildAdvertisement().Value;

        // A headless worker MUST satisfy both conditions simultaneously
        (ad.HeadlessOnly && ad.InteractiveSessionId is null).Should().BeTrue(
            "both HeadlessOnly=true AND InteractiveSessionId=null must hold for AC-5 compliance");
    }
}
