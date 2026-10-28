namespace Tracto.Designer.Core.Testing;

using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Validation;

public sealed class LocalSimulationTestRunner(IScriptValidator validator) : IScriptTestRunner
{
    public string Name => "Local Simulation";

    public async Task<ScriptTestResult> RunAsync(Script script, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var validation = validator.Validate(script);
        if (!validation.IsValid)
        {
            var firstError = validation.Errors.FirstOrDefault(e => e.Severity == ValidationSeverity.Error);
            var message = firstError?.Message ?? validation.Errors.FirstOrDefault()?.Message ?? "Validation failed with unspecified errors.";
            return ScriptTestResult.ValidationFailure($"Validation failed: {message}");
        }

        var stepResults = new List<StepTestResult>();
        var total = script.Steps.Count;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            for (int i = 0; i < total; i++)
            {
                ct.ThrowIfCancellationRequested();
                var stepSw = System.Diagnostics.Stopwatch.StartNew();
                await Task.Delay(50, ct).ConfigureAwait(false);
                stepSw.Stop();
                stepResults.Add(new StepTestResult(script.Steps[i].StepNumber, true, "Simulated OK", stepSw.Elapsed));
                progress?.Report((i + 1) * 100 / total);
            }
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return new ScriptTestResult(false, stepResults, sw.Elapsed, "Test run cancelled.");
        }

        sw.Stop();
        return new ScriptTestResult(true, stepResults, sw.Elapsed);
    }
}
