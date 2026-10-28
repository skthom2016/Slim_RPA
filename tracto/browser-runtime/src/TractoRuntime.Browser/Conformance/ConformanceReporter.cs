using Microsoft.Extensions.Logging;
using TractoRuntime.Browser.Validation;

namespace TractoRuntime.Browser.Conformance;

/// <summary>
/// Production implementation of <see cref="IConformanceReporter"/>.
///
/// <para>Runs the full CF-01–CF-10 CEC v1.0.1 conformance suite against in-process
/// implementations of the Browser Runtime contract layer, then emits a structured report.
/// All fixtures run without a live browser or network connection (mock Orchestrator mode).</para>
///
/// <para>Authority: BRW-US-020 AC#1, CEC-007, CEC-010, Appendix F.</para>
/// </summary>
public sealed class ConformanceReporter(
    ILogger<ConformanceReporter> logger,
    IArtifactValidator artifactValidator) : IConformanceReporter
{
    /// <inheritdoc/>
    public async Task ReportConformanceAsync(
        string workerId,
        string runtimeVersion,
        CancellationToken ct)
    {
        logger.LogInformation(
            "Conformance suite starting — workerId={WorkerId}, runtimeVersion={Version}",
            workerId, runtimeVersion);

        var runner  = new ConformanceRunner(artifactValidator, logger);
        var results = await runner.RunAllAsync(ct);

        // Report each fixture result
        foreach (var r in results)
        {
            if (r.Passed)
            {
                logger.LogInformation(
                    "[PASS] {FixtureId} — {Description}", r.FixtureId, r.Description);
            }
            else
            {
                logger.LogError(
                    "[FAIL] {FixtureId} — {Description} — reason: {Reason}",
                    r.FixtureId, r.Description, r.FailureReason);
            }
        }

        int passed = results.Count(r => r.Passed);
        int total  = results.Count;
        int failed = total - passed;

        logger.LogInformation(
            "Conformance suite complete — workerId={WorkerId}, runtimeVersion={Version}, " +
            "passed={Passed}/{Total}",
            workerId, runtimeVersion, passed, total);

        if (failed > 0)
        {
            var failedIds = string.Join(", ",
                results.Where(r => !r.Passed).Select(r => r.FixtureId));

            throw new ConformanceSuiteException(
                $"CEC v1.0.1 conformance suite FAILED: {failed} fixture(s) did not pass " +
                $"({failedIds}). The Browser Runtime may not be released until all fixtures pass.");
        }
    }
}
