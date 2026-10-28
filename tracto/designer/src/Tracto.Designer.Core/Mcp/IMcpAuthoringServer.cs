namespace Tracto.Designer.Core.Mcp;

public interface IMcpAuthoringServer : IDisposable
{
    bool IsRunning { get; }
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    string? EndpointAddress { get; }
}
