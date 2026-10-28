using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Tracto.Desktop.Ipc;

/// <summary>
/// Named-pipe server endpoint owned exclusively by the Supervisor process.
/// Accepts exactly one Executor client connection; access is restricted to the
/// current Windows user SID via OS-enforced <see cref="PipeSecurity"/> ACL.
/// </summary>
/// <remarks>
/// This class intentionally contains no GUI-input APIs (SendInput, mouse_event,
/// keybd_event, SetForegroundWindow, etc.).  The Supervisor cannot inject desktop
/// events by design — that boundary is enforced structurally, not by policy alone.
/// </remarks>
public sealed class SupervisorPipeServer : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private bool _disposed;

    /// <summary>Name of the pipe as registered with the OS.</summary>
    public string PipeName { get; }

    /// <summary>
    /// Initialises the server stream.  The pipe is not yet listening; call
    /// <see cref="AcceptConnectionAsync"/> to block until the Executor connects.
    /// </summary>
    /// <param name="pipeName">
    /// Fully-qualified pipe name, e.g. <c>tracto-supervisor-{MachineName}</c>.
    /// Pass <see langword="null"/> to use the default machine-scoped name.
    /// </param>
    public SupervisorPipeServer(string? pipeName = null)
    {
        PipeName = pipeName ?? $"tracto-supervisor-{Environment.MachineName}";

        var pipeSecurity = new PipeSecurity();
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            WindowsIdentity.GetCurrent().User!,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        _pipe = NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            pipeSecurity);
    }

    /// <summary>Blocks asynchronously until an Executor client connects or <paramref name="ct"/> is cancelled.</summary>
    public async Task AcceptConnectionAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Sends <paramref name="msg"/> over the pipe using a 4-byte little-endian length prefix.</summary>
    public async Task SendMessageAsync(IpcMessage msg, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await WriteFrameAsync(_pipe, msg, ct).ConfigureAwait(false);
    }

    /// <summary>Reads the next framed message from the pipe and deserializes it.</summary>
    public async Task<IpcMessage> ReceiveMessageAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await ReadFrameAsync(_pipe, ct).ConfigureAwait(false);
    }

    // ── internal framing helpers shared with ExecutorPipeClient ───────────────

    internal static async Task WriteFrameAsync(Stream stream, IpcMessage msg, CancellationToken ct)
    {
        byte[] payload = IpcSerializer.Serialize(msg);
        byte[] lengthPrefix = BitConverter.GetBytes(payload.Length); // 4 bytes, LE on all modern .NET
        await stream.WriteAsync(lengthPrefix, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    internal static async Task<IpcMessage> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        byte[] lengthBuf = new byte[4];
        await ReadExactAsync(stream, lengthBuf, ct).ConfigureAwait(false);
        int length = BitConverter.ToInt32(lengthBuf, 0);
        if (length <= 0 || length > 1_048_576) // 1 MB sanity cap
            throw new InvalidDataException($"IPC frame length {length} is out of the permitted range.");

        byte[] payloadBuf = new byte[length];
        await ReadExactAsync(stream, payloadBuf, ct).ConfigureAwait(false);
        return IpcSerializer.Deserialize(payloadBuf);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Named pipe closed before the full IPC frame was received.");
            offset += read;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pipe.Dispose();
    }
}
