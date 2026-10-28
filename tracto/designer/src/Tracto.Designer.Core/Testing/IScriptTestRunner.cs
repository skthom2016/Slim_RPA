namespace Tracto.Designer.Core.Testing;

using Tracto.Designer.Core.Scripting;

public interface IScriptTestRunner
{
    string Name { get; }
    Task<ScriptTestResult> RunAsync(Script script, IProgress<int>? progress = null, CancellationToken ct = default);
}
