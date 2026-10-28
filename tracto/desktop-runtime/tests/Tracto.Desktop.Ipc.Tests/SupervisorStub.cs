using Tracto.Desktop.Ipc;

namespace Tracto.Desktop.Ipc.Tests;

/// <summary>
/// In-process test helper that starts a <see cref="SupervisorPipeServer"/> in a background task
/// and automatically completes the IPC handshake when an Executor client connects.
/// Dispose to shut down the background server cleanly.
/// </summary>
public sealed class SupervisorStub : IAsyncDisposable
{
    private readonly SupervisorPipeServer _server;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _backgroundTask;

    /// <summary>Unique pipe name generated for this stub instance (avoids collision between parallel tests).</summary>
    public string PipeName { get; }

    /// <summary>The <see cref="OrchestratorEndpoint"/> this stub will advertise to the connecting Executor.</summary>
    public OrchestratorEndpoint Endpoint { get; }

    /// <summary>
    /// Initialises and starts the background server.
    /// </summary>
    /// <param name="endpoint">Endpoint to advertise; defaults to a local test endpoint if <see langword="null"/>.</param>
    public SupervisorStub(OrchestratorEndpoint? endpoint = null)
    {
        PipeName = $"tracto-stub-{Guid.NewGuid():N}";
        Endpoint = endpoint ?? new OrchestratorEndpoint("localhost", 9090, "grpc");
        _server = new SupervisorPipeServer(PipeName);
        _backgroundTask = Task.Run(RunAsync);
    }

    private async Task RunAsync()
    {
        try
        {
            await _server.AcceptConnectionAsync(_cts.Token).ConfigureAwait(false);
            await IpcHandshakeProtocol.SupervisorHandshakeAsync(_server, Endpoint, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* clean shutdown */ }
        catch (Exception)
        {
            /* Swallow to prevent unobserved task exceptions during teardown; test assertions are on the client side. */
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try { await _backgroundTask.ConfigureAwait(false); } catch { /* already swallowed */ }
        _cts.Dispose();
        _server.Dispose();
    }
}
