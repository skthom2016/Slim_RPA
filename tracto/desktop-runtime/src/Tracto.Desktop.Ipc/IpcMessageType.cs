namespace Tracto.Desktop.Ipc;

/// <summary>Discriminated union of all named-pipe IPC message types exchanged between Supervisor and Executor.</summary>
public enum IpcMessageType
{
    /// <summary>Executor → Supervisor: request to register and receive the Orchestrator endpoint.</summary>
    Register,
    /// <summary>Supervisor → Executor: acknowledgement of registration, carries OrchestratorEndpoint JSON in payload.</summary>
    RegisterAck,
    /// <summary>Executor → Supervisor: request to pull the next job from the Orchestrator.</summary>
    JobPull,
    /// <summary>Supervisor → Executor: a job offer containing job descriptor JSON in payload.</summary>
    JobOffer,
    /// <summary>Executor → Supervisor: periodic heartbeat to confirm the Executor is alive.</summary>
    Heartbeat,
    /// <summary>Supervisor → Executor: acknowledgement of a heartbeat.</summary>
    HeartbeatAck,
    /// <summary>Supervisor → Executor: instruction to cancel the currently executing job.</summary>
    Cancel,
    /// <summary>Executor → Supervisor: acknowledgement that cancellation has been processed.</summary>
    CancelAck
}
