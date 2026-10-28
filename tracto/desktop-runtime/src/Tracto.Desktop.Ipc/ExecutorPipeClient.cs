using System.IO.Pipes;

namespace Tracto.Desktop.Ipc;

/// <summary>
/// Named-pipe client used exclusively by the Executor process to connect to the
/// Supervisor's <see cref="SupervisorPipeServer"/>.
/// </summary>
public sealed class ExecutorPipeClient : IDisposable
{
    private NamedPipeClientStream? _pipe;
    private bool _disposed;

    /// <summary>
    /// Creates a new pipe client and connects to <paramref name="pipeName"/> on the local machine.
    /// </summary>
    /// <param name="pipeName">Pipe name to connect to (e.g. <c>tracto-supervisor-{MachineName}</c>).</param>
    /// <param name="timeoutMs">Maximum wait in milliseconds for the server to be available.</param>
    /// <param name="ct">Cancellation token; an already-cancelled token causes immediate <see cref="OperationCanceledException"/>.</param>
    public async Task ConnectAsync(string pipeName, int timeoutMs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        _pipe = new NamedPipeClientStream(
            serverName: ".",
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);

        await _pipe.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);
    }

    /// <summary>Sends <paramref name="msg"/> over the pipe using a 4-byte little-endian length prefix.</summary>
    public async Task SendMessageAsync(IpcMessage msg, CancellationToken ct = default)
    {
        EnsureConnected();
        await SupervisorPipeServer.WriteFrameAsync(_pipe!, msg, ct).ConfigureAwait(false);
    }

    /// <summary>Reads the next framed message from the pipe and deserializes it.</summary>
    public async Task<IpcMessage> ReceiveMessageAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        return await SupervisorPipeServer.ReadFrameAsync(_pipe!, ct).ConfigureAwait(false);
    }

    private void EnsureConnected()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pipe is null || !_pipe.IsConnected)
            throw new InvalidOperationException("ExecutorPipeClient is not connected. Call ConnectAsync first.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pipe?.Dispose();
    }
}
