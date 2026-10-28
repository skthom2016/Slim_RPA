#nullable enable

using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Tracto.Conformance.Runner;
using Tracto.Contract.Testing;
using Tracto.Contract.Validation;
using Tracto.Mock.Orchestrator;
using Xunit;

namespace Tracto.Contract.Schemas.Tests;

/// <summary>
/// Sprint 6 conformance runner tests — ST1-14 ACs 01-09.
/// Each test drives one or more ConformanceScenarios through the runner and
/// asserts the expected outcome.
/// </summary>
public sealed class ConformanceRunnerTests
{
    private ConformanceRunner Runner => new(new FakeTimeProvider());

    // -----------------------------------------------------------------------
    // ST1-14-AC01: CF-01 happy path passes
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CF01_HappyPath_Passes()
    {
        var mock = new MockOrchestrator(clock: new FakeTimeProvider());
        var report = await Runner.RunAsync(new[] { ConformanceScenarioLibrary.CF01_HappyPath }, mock);
        report.AllPassed.Should().BeTrue(because: report.Results[0].FailureReason ?? "");
    }

    // -----------------------------------------------------------------------
    // ST1-14-AC02: CF-02 incompatible schema → registration refused
    // A WorkerRegistrationValidator is required so the schema-major rule fires.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CF02_IncompatibleSchema_Passes()
    {
        var validator = await WorkerRegistrationValidator.CreateAsync();
        var mock = new MockOrchestrator(registrationValidator: validator, clock: new FakeTimeProvider());
        var report = await Runner.RunAsync(new[] { ConformanceScenarioLibrary.CF02_IncompatibleSchemaMajor }, mock);
        report.AllPassed.Should().BeTrue(because: report.Results[0].FailureReason ?? "");
    }

    // -----------------------------------------------------------------------
    // ST1-14-AC03: CF-03 uncertain expiry → UNKNOWN reconcile
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CF03_UncertainExpiry_Passes()
    {
        var mock = new MockOrchestrator(clock: new FakeTimeProvider());
        var report = await Runner.RunAsync(new[] { ConformanceScenarioLibrary.CF03_UncertainLeaseExpiry }, mock);
        report.AllPassed.Should().BeTrue(because: report.Results[0].FailureReason ?? "");
    }

    // -----------------------------------------------------------------------
    // ST1-14-AC04: CF-04 duplicate heartbeat deduplication
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CF04_DuplicateHeartbeat_Passes()
    {
        var mock = new MockOrchestrator(clock: new FakeTimeProvider());
        var report = await Runner.RunAsync(new[] { ConformanceScenarioLibrary.CF04_DuplicateHeartbeat }, mock);
        report.AllPassed.Should().BeTrue(because: report.Results[0].FailureReason ?? "");
    }

    // -----------------------------------------------------------------------
    // ST1-14-AC04: CF-05 unknown lease reconcile
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CF05_UnknownLease_Passes()
    {
        var mock = new MockOrchestrator(clock: new FakeTimeProvider());
        var report = await Runner.RunAsync(new[] { ConformanceScenarioLibrary.CF05_UnknownLeaseReconcile }, mock);
        report.AllPassed.Should().BeTrue(because: report.Results[0].FailureReason ?? "");
    }

    // -----------------------------------------------------------------------
    // ST1-14-AC05: CF-06 service SUCCESS outcome
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CF06_ServiceSuccess_Passes()
    {
        var mock = new MockOrchestrator(clock: new FakeTimeProvider());
        var report = await Runner.RunAsync(new[] { ConformanceScenarioLibrary.CF06_ServiceSuccess }, mock);
        report.AllPassed.Should().BeTrue(because: report.Results[0].FailureReason ?? "");
    }

    // -----------------------------------------------------------------------
    // ST1-14-AC05: CF-07 service TRANSIENT_FAILURE
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CF07_ServiceTransientFailure_Passes()
    {
        var mock = new MockOrchestrator(clock: new FakeTimeProvider());
        var report = await Runner.RunAsync(new[] { ConformanceScenarioLibrary.CF07_ServiceTransientFailure }, mock);
        report.AllPassed.Should().BeTrue(because: report.Results[0].FailureReason ?? "");
    }

    // -----------------------------------------------------------------------
    // ST1-14-AC06: CF-08 security boundary (plaintext secret rejected)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CF08_SecurityBoundary_Passes()
    {
        var mock = new MockOrchestrator(clock: new FakeTimeProvider());
        var report = await Runner.RunAsync(new[] { ConformanceScenarioLibrary.CF08_SecurityPlaintextSecret }, mock);
        report.AllPassed.Should().BeTrue(because: report.Results[0].FailureReason ?? "");
    }

    // -----------------------------------------------------------------------
    // ST1-14-AC06: CF-10 unapproved serviceRef rejected
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CF10_UnapprovedServiceRef_Passes()
    {
        var mock = new MockOrchestrator(clock: new FakeTimeProvider());
        var report = await Runner.RunAsync(new[] { ConformanceScenarioLibrary.CF10_UnapprovedServiceRef }, mock);
        report.AllPassed.Should().BeTrue(because: report.Results[0].FailureReason ?? "");
    }

    // -----------------------------------------------------------------------
    // ST1-14-AC07: CF-09 safe cancel and disconnection
    // -----------------------------------------------------------------------
    [Fact]
    public async Task CF09_SafeCancel_Passes()
    {
        var mock = new MockOrchestrator(clock: new FakeTimeProvider());
        var report = await Runner.RunAsync(new[] { ConformanceScenarioLibrary.CF09_SafeCancel }, mock);
        report.AllPassed.Should().BeTrue(because: report.Results[0].FailureReason ?? "");
    }

    // -----------------------------------------------------------------------
    // ST1-14-AC08: Per-runtime independence — each scenario has a unique FixtureId
    // -----------------------------------------------------------------------
    [Fact]
    public void AllScenarios_HaveUniqueFixtureIds()
    {
        var ids = ConformanceScenarioLibrary.AllScenarios.Select(s => s.FixtureId).ToList();
        ids.Should().OnlyHaveUniqueItems(because: "each scenario must have a unique fixture ID");
    }

    // -----------------------------------------------------------------------
    // ST1-14-AC09: Deterministic repeatability — two independent runs produce
    // the same pass/fail distribution. Each scenario runs against its own fresh
    // MockOrchestrator (server = null) so scenario state never bleeds across.
    // CF-02 uses a plain mock and is expected to fail consistently in this
    // bulk run (its individual test above uses a validator-equipped mock).
    // The contract under test is REPEATABILITY, not that every scenario passes.
    // -----------------------------------------------------------------------
    [Fact]
    public async Task AllScenarios_AreRepeatableAndAllPass()
    {
        // Run 1 — each scenario gets its own fresh MockOrchestrator via server=null
        var report1 = await Runner.RunAsync(ConformanceScenarioLibrary.AllScenarios);

        // Run 2 — fresh runner, same seed time, same scenario list
        var report2 = await new ConformanceRunner(new FakeTimeProvider())
            .RunAsync(ConformanceScenarioLibrary.AllScenarios);

        // Both runs must produce identical pass/fail counts (determinism).
        report1.PassCount.Should().Be(
            report2.PassCount,
            because: "conformance results must be deterministic across independent runs");
        report1.FailCount.Should().Be(
            report2.FailCount,
            because: "conformance results must be deterministic across independent runs");

        // All scenarios that do NOT require a validator must pass.
        // (CF-02 requires a validator-equipped mock; its own dedicated test verifies it.)
        var nonValidatorResults = report1.Results
            .Where(r => r.FixtureId != "CF-02")
            .ToList();
        nonValidatorResults.Should().AllSatisfy(
            r => r.Passed.Should().BeTrue(because: r.FailureReason ?? ""),
            because: "all scenarios except CF-02 must pass with a default mock");
    }
}
