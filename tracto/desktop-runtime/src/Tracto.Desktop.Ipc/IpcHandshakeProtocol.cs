namespace Tracto.Desktop.Ipc;

/// <summary>
/// Stateless helpers that implement the two-phase Register/RegisterAck handshake
/// between the Supervisor and Executor over the named-pipe IPC channel.
/// </summary>
public static class IpcHandshakeProtocol
{
    /// <summary>
    /// Executor side of the handshake: sends a <see cref="IpcMessageType.Register"/> message and
    /// awaits a <see cref="IpcMessageType.RegisterAck"/> reply containing the
    /// <see cref="OrchestratorEndpoint"/> JSON in its payload.
    /// </summary>
    /// <param name="client">An already-connected <see cref="ExecutorPipeClient"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="OrchestratorEndpoint"/> the Supervisor reported.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the reply message type is not <see cref="IpcMessageType.RegisterAck"/>
    /// or the payload cannot be deserialized to an <see cref="OrchestratorEndpoint"/>.
    /// </exception>
    public static async Task<OrchestratorEndpoint> ExecutorHandshakeAsync(
        ExecutorPipeClient client,
        CancellationToken ct = default)
    {
        string correlationId = Guid.NewGuid().ToString("N");

        var registerMsg = new IpcMessage(IpcMessageType.Register, string.Empty, correlationId);
        await client.SendMessageAsync(registerMsg, ct).ConfigureAwait(false);

        IpcMessage ack = await client.ReceiveMessageAsync(ct).ConfigureAwait(false);
        if (ack.Type != IpcMessageType.RegisterAck)
            throw new InvalidDataException(
                $"Expected RegisterAck but received {ack.Type}. CorrelationId={ack.CorrelationId}");

        return IpcSerializer.DeserializeEndpoint(ack.Payload);
    }

    /// <summary>
    /// Supervisor side of the handshake: awaits a <see cref="IpcMessageType.Register"/> message
    /// and replies with a <see cref="IpcMessageType.RegisterAck"/> containing the serialized
    /// <paramref name="endpoint"/> as the payload.
    /// </summary>
    /// <param name="server">
    /// A <see cref="SupervisorPipeServer"/> that has already accepted a client connection via
    /// <see cref="SupervisorPipeServer.AcceptConnectionAsync"/>.
    /// </param>
    /// <param name="endpoint">The <see cref="OrchestratorEndpoint"/> to advertise to the Executor.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidDataException">
    /// Thrown when the received message type is not <see cref="IpcMessageType.Register"/>.
    /// </exception>
    public static async Task SupervisorHandshakeAsync(
        SupervisorPipeServer server,
        OrchestratorEndpoint endpoint,
        CancellationToken ct = default)
    {
        IpcMessage registerMsg = await server.ReceiveMessageAsync(ct).ConfigureAwait(false);
        if (registerMsg.Type != IpcMessageType.Register)
            throw new InvalidDataException(
                $"Expected Register but received {registerMsg.Type}. CorrelationId={registerMsg.CorrelationId}");

        string endpointJson = IpcSerializer.SerializeEndpoint(endpoint);
        var ack = new IpcMessage(IpcMessageType.RegisterAck, endpointJson, registerMsg.CorrelationId);
        await server.SendMessageAsync(ack, ct).ConfigureAwait(false);
    }
}
