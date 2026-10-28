#nullable enable

namespace Tracto.Desktop.Protocol;

/// <summary>
/// Holds the identifiers established when the Desktop Runtime accepts a lease.
/// Created by <see cref="JobPullLoop"/> when a JobOffered response is received and AcceptLease has been called.
/// </summary>
public sealed class JobContext
{
    /// <summary>The lease identifier assigned by the Orchestrator.</summary>
    public required string LeaseId { get; init; }

    /// <summary>The attempt identifier for this execution.</summary>
    public required string AttemptId { get; init; }

    /// <summary>The run identifier.</summary>
    public required string RunId { get; init; }

    /// <summary>URL from which to download the automation artifact.</summary>
    public required string ArtifactUrl { get; init; }

    /// <summary>SHA-256 digest of the artifact for integrity verification.</summary>
    public required string ArtifactDigest { get; init; }

    /// <summary>Lease duration in milliseconds; heartbeats must be sent within this interval.</summary>
    public required int LeaseDurationMs { get; init; }
}
