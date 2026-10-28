namespace Tracto.Orchestrator.Domain.Settings;

/// <summary>
/// Controls how the Orchestrator binds its HTTP listener and connects to external services.
/// </summary>
public enum DeploymentMode
{
    /// <summary>
    /// Orchestrator binds on 0.0.0.0 and accepts connections from the network.
    /// Intended for multi-machine RPA fleets.
    /// </summary>
    Connected,

    /// <summary>
    /// Orchestrator binds on 127.0.0.1 only. All traffic stays on the local machine.
    /// Intended for single-machine or air-gapped scenarios.
    /// </summary>
    Isolated
}
