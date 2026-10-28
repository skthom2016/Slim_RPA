using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Worker;

public interface ICecWorker
{
    string WorkerId { get; }
    Task RegisterAsync(CancellationToken ct);
    Task<CecRunResult> ExecuteRunAsync(CecRunRequest request, CancellationToken ct);
    Task UnregisterAsync(CancellationToken ct);
}
