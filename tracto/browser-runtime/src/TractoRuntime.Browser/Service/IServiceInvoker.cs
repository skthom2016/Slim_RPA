using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Service;

public interface IServiceInvoker
{
    Task<ServiceInvokeResult> InvokeAsync(ServiceInvokeRef serviceRef, Dictionary<string, object?> input, CancellationToken ct);
}

public sealed record ServiceInvokeResult
{
    public required ServiceOutcome Outcome { get; init; }
    public string? ActionCode { get; init; }
    public Dictionary<string, object?> Output { get; init; } = new();
}
