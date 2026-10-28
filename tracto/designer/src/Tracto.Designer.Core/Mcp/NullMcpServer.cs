namespace Tracto.Designer.Core.Mcp;

public sealed class NullMcpServer : IMcpAuthoringServer
{
    public bool IsRunning => false;
    public string? EndpointAddress => null;
    public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Dispose() { }
}
