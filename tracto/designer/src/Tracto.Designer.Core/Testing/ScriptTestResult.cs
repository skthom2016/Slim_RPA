namespace Tracto.Designer.Core.Testing;

public sealed class ScriptTestResult
{
    public bool Passed { get; }
    public IReadOnlyList<StepTestResult> StepResults { get; }
    public TimeSpan Duration { get; }
    public string? ErrorMessage { get; }

    public ScriptTestResult(bool passed, IEnumerable<StepTestResult> steps, TimeSpan duration, string? error = null)
    {
        Passed = passed;
        StepResults = [..steps];
        Duration = duration;
        ErrorMessage = error;
    }

    public static ScriptTestResult ValidationFailure(string msg) =>
        new(false, [], TimeSpan.Zero, msg);

    /// <summary>
    /// Sentinel used when deployment policy does not require a live test run.
    /// The publication gate will not evaluate this result when <c>testRequired = false</c>.
    /// </summary>
    public static ScriptTestResult NotRun { get; } =
        new(false, [], TimeSpan.Zero, "Not run — testing not required by deployment policy");
}
