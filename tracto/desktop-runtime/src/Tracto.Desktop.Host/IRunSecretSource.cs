#nullable enable
using Tracto.Desktop.Protocol;

namespace Tracto.Desktop.Host;

/// <summary>Where a run gets the environment variables its script reads.</summary>
internal interface IRunSecretSource
{
    /// <summary>
    /// The variables a running job reads, in one call. Throws <see cref="CredentialRefusedException"/>
    /// when the Orchestrator refuses (the message names the variable at fault).
    /// </summary>
    Task<IReadOnlyDictionary<string, EnvValue>> GetEnvAsync(
        string runId, IReadOnlyCollection<string> names, CancellationToken ct);
}
