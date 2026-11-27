#nullable enable
using Tracto.Desktop.Protocol;

namespace Tracto.Desktop.Host;

/// <summary>Where a run gets the credentials and settings its script reads.</summary>
internal interface IRunSecretSource
{
    /// <summary>
    /// The credentials a running job reads. Throws <see cref="CredentialRefusedException"/> when the
    /// Orchestrator refuses (the message names the credential at fault).
    /// </summary>
    Task<IReadOnlyDictionary<string, CredentialValues>> GetCredentialsAsync(
        string runId, IReadOnlyCollection<string> names, CancellationToken ct);

    /// <summary>The settings a running job reads, under the same checks.</summary>
    Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(
        string runId, IReadOnlyCollection<string> names, CancellationToken ct);
}