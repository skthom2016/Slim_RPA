namespace Tracto.Desktop.Ipc;

/// <summary>
/// Immutable descriptor of the Orchestrator network endpoint delivered to the Executor during the IPC handshake.
/// The Executor cannot mutate or override these values — they originate exclusively from the Supervisor.
/// </summary>
/// <param name="Host">Hostname or IP address of the Orchestrator (e.g. "localhost" or WSL2 gateway address).</param>
/// <param name="Port">TCP port on which the Orchestrator listens.</param>
/// <param name="Scheme">URI scheme used by the Orchestrator protocol, typically "https" or "grpc".</param>
public sealed record OrchestratorEndpoint(string Host, int Port, string Scheme);
