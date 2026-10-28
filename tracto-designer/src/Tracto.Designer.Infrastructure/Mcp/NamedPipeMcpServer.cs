namespace Tracto.Designer.Infrastructure.Mcp;

using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Tracto.Designer.Core.Mcp;

public sealed class NamedPipeMcpServer : IMcpAuthoringServer
{
    private const string PipeName = "tracto-designer-mcp";
    private readonly McpToolHandler _handler;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;

    public NamedPipeMcpServer(McpToolHandler handler) => _handler = handler;

    public bool IsRunning => _cts is not null && !_cts.IsCancellationRequested;
    public string? EndpointAddress => IsRunning ? $@"\\.\pipe\{PipeName}" : null;

    public Task StartAsync(CancellationToken ct = default)
    {
        if (IsRunning) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _serverTask = RunServerLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_cts is not null) { _cts.Cancel(); _cts.Dispose(); _cts = null; }
        if (_serverTask is not null) { try { await _serverTask.ConfigureAwait(false); } catch { } _serverTask = null; }
    }

    private async Task RunServerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                await HandleConnectionAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch { /* client disconnect — restart loop */ }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var req  = JsonDocument.Parse(line);
                string tool = req.RootElement.GetProperty("tool").GetString() ?? string.Empty;
                string? args = req.RootElement.TryGetProperty("args", out var a) ? a.GetRawText() : null;
                var result = _handler.HandleTool(tool, args);
                var resp = JsonSerializer.Serialize(new { success = result.Success, content = result.Content, error = result.Error });
                await writer.WriteLineAsync(resp).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var err = JsonSerializer.Serialize(new { success = false, content = string.Empty, error = ex.Message });
                await writer.WriteLineAsync(err).ConfigureAwait(false);
            }
        }
    }

    public void Dispose() { _cts?.Cancel(); _cts?.Dispose(); }
}
