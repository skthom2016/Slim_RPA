using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Tests.Fixtures;

/// <summary>Factory for test fixtures — canonical contract objects for unit tests.</summary>
public static class TestArtifactFactory
{
    public static CecRunRequest MakeRunRequest(string? runId = null, string? scriptRef = null)
        => new()
        {
            RunId = runId ?? $"run-{Guid.NewGuid():N}",
            WorkerId = "test-worker-001",
            ScriptRef = scriptRef ?? "test://scripts/noop-v1"
        };

    public static BrowserScript MakeScript(BrowserMode mode = BrowserMode.Headless, int stepCount = 1)
        => new()
        {
            ScriptId = $"script-{Guid.NewGuid():N}",
            Version = "1.0.0",
            Mode = mode,
            Steps = Enumerable.Range(1, stepCount).Select(i => MakeStep($"step-{i:D3}")).ToList()
        };

    public static BrowserStep MakeStep(string? stepId = null, string action = "browser.navigate")
        => new()
        {
            StepId = stepId ?? $"step-{Guid.NewGuid():N}",
            Action = action
        };

    public static BrowserStep MakeClickStep(string? css = null)
        => new()
        {
            StepId = $"step-{Guid.NewGuid():N}",
            Action = "browser.click",
            Selector = new SelectorRef { Css = css ?? "button[data-testid='submit']" }
        };

    public static WorkerRegistration MakeWorkerRegistration(string? workerId = null)
        => new()
        {
            WorkerId = workerId ?? "test-worker-001",
            RuntimeType = "browser",
            Capabilities = ["browser.headed", "browser.headless"]
        };

    public static CecRunResult MakeSuccessResult(string runId)
        => new()
        {
            RunId = runId,
            State = RunState.SUCCEEDED,
            ServiceOutcome = ServiceOutcome.SUCCESS,
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
            EndedAt = DateTimeOffset.UtcNow
        };

    public static CecRunResult MakeFailedResult(string runId, ErrorCategory category, string message)
        => new()
        {
            RunId = runId,
            State = RunState.FAILED,
            Error = new CecError { Category = category, Message = message },
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
            EndedAt = DateTimeOffset.UtcNow
        };
}
