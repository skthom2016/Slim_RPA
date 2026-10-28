using System.Text;
using System.Text.Json;

namespace Tracto.Desktop.Ipc;

/// <summary>
/// Thread-safe, allocation-minimal JSON serialization helpers for <see cref="IpcMessage"/>
/// and related IPC payload types.  All methods are static so callers need no instance.
/// </summary>
public static class IpcSerializer
{
    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Serializes <paramref name="message"/> to a UTF-8 JSON byte array.</summary>
    public static byte[] Serialize(IpcMessage message) =>
        JsonSerializer.SerializeToUtf8Bytes(message, s_options);

    /// <summary>Deserializes an <see cref="IpcMessage"/> from a UTF-8 JSON byte span.</summary>
    public static IpcMessage Deserialize(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize<IpcMessage>(utf8Json, s_options)
        ?? throw new InvalidDataException("IPC JSON payload deserialized to null.");

    /// <summary>Serializes <paramref name="endpoint"/> to a compact JSON string (used as message payload).</summary>
    public static string SerializeEndpoint(OrchestratorEndpoint endpoint) =>
        JsonSerializer.Serialize(endpoint, s_options);

    /// <summary>Deserializes an <see cref="OrchestratorEndpoint"/> from a JSON string payload.</summary>
    public static OrchestratorEndpoint DeserializeEndpoint(string json) =>
        JsonSerializer.Deserialize<OrchestratorEndpoint>(json, s_options)
        ?? throw new InvalidDataException("OrchestratorEndpoint JSON payload deserialized to null.");
}
