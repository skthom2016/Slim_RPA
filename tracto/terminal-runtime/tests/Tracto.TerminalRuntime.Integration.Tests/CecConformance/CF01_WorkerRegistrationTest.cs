using FluentAssertions;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Integration.Tests.CecConformance;

/// <summary>
/// CF-01 — Worker Registration
///
/// A Terminal worker must register with a capability advertisement that satisfies:
///   - RuntimeType = "terminal"   (CEC C4)
///   - HeadlessOnly = true        (CEC C4, AC-5 headless gate)
///   - InteractiveSessionId = null (CEC C4, AC-5)
///   - SupportedShells is not empty
///
/// This test runs against the <see cref="MockOrchestratorFixture"/>; no real
/// Orchestrator is required.
/// </summary>
public sealed class CF01_WorkerRegistrationTest : IClassFixture<MockOrchestratorFixture>
{
    private readonly MockOrchestratorFixture _fx;

    public CF01_WorkerRegistrationTest(MockOrchestratorFixture fx) => _fx = fx;

    [Fact(DisplayName = "CF-01-A: BuildAdvertisement succeeds")]
    public void BuildAdvertisement_Succeeds()
    {
        var result = _fx.CapabilityAdvertiser.BuildAdvertisement();
        result.IsSuccess.Should().BeTrue("capability advertiser must not fail with valid configuration");
    }

    [Fact(DisplayName = "CF-01-B: RuntimeType is 'terminal'")]
    public void BuildAdvertisement_RuntimeType_IsTerminal()
    {
        var ad = _fx.CapabilityAdvertiser.BuildAdvertisement().Value;
        ad.RuntimeType.Should().Be(
            WorkerCapabilityAdvertisement.TerminalRuntimeType,
            "CEC C4 requires RuntimeType = \"terminal\"");
    }

    [Fact(DisplayName = "CF-01-C: HeadlessOnly is true")]
    public void BuildAdvertisement_HeadlessOnly_IsTrue()
    {
        var ad = _fx.CapabilityAdvertiser.BuildAdvertisement().Value;
        ad.HeadlessOnly.Should().BeTrue(
            "CEC C4 and AC-5 require that the Terminal worker never needs an interactive session");
    }

    [Fact(DisplayName = "CF-01-D: InteractiveSessionId is null")]
    public void BuildAdvertisement_InteractiveSessionId_IsNull()
    {
        var ad = _fx.CapabilityAdvertiser.BuildAdvertisement().Value;
        ad.InteractiveSessionId.Should().BeNull(
            "CEC C4 requires that no foreground desktop input lease is held");
    }

    [Fact(DisplayName = "CF-01-E: SupportedShells is not empty")]
    public void BuildAdvertisement_SupportedShells_NotEmpty()
    {
        var ad = _fx.CapabilityAdvertiser.BuildAdvertisement().Value;
        ad.SupportedShells.Should().NotBeEmpty(
            "at least one supported shell must be advertised");
    }

    [Fact(DisplayName = "CF-01-F: WorkerId is not empty")]
    public void BuildAdvertisement_WorkerId_NotEmpty()
    {
        var ad = _fx.CapabilityAdvertiser.BuildAdvertisement().Value;
        ad.WorkerId.Should().NotBeNullOrWhiteSpace(
            "WorkerId must be a non-blank identifier");
    }
}
