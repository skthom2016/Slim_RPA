namespace Tracto.Desktop.Ipc;

/// <summary>
/// Immutable message exchanged over the named-pipe IPC channel between Supervisor and Executor.
/// Serialized as JSON with a 4-byte little-endian length prefix on the wire.
/// </summary>
/// <param name="Type">Discriminator identifying the purpose of this message.</param>
/// <param name="Payload">JSON-encoded payload specific to the message type; empty string when unused.</param>
/// <param name="CorrelationId">Opaque identifier used to match requests with their acknowledgements.</param>
public sealed record IpcMessage(IpcMessageType Type, string Payload, string CorrelationId);
