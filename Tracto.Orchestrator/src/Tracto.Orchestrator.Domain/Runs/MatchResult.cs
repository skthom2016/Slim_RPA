// MatchResult and its cases (JobOffered, NoPendingJob, QueueFull) have been
// consolidated into Tracto.Orchestrator.Domain.Leases.  This file is intentionally
// emptied to prevent duplicate-type compile errors.
namespace Tracto.Orchestrator.Domain.Runs;
#if false // canonical types live in Domain.Leases
#nullable enable

/// <summary>
/// Discriminated union for the outcome of a PullJob operation (OC-03).
/// Implemented as a sealed class hierarchy so pattern-matching is exhaustive.
/// </summary>
public abstract record MatchResult;

/// <summary>
/// A suitable queued job was found and a lease has been offered to the worker.
/// The worker must acknowledge (accept) the lease within the offer window (30 s).
/// </summary>
/// <param name="RunId">The matched run's identifier.</param>
/// <param name="AttemptId">
/// The AttemptId pinned at offer time — must be echoed back in all subsequent calls.
/// </param>
/// <param name="LeaseId">Unique identifier of the created <see cref="LeaseRecord"/>.</param>
/// <param name="ArtifactId">Script artifact identifier the worker should execute.</param>
/// <param name="ArtifactVersion">Pinned artifact version.</param>
/// <param name="ArtifactHash">SHA-256 content hash for integrity verification.</param>
/// <param name="InputRefs">Opaque references to input blobs for this run.</param>
/// <param name="SecretRefs">Opaque secret references — no actual secret values.</param>
public sealed record JobOffered(
    Guid     RunId,
    Guid     AttemptId,
    Guid     LeaseId,
    string   ArtifactId,
    string   ArtifactVersion,
    string   ArtifactHash,
    string[] InputRefs,
    string[] SecretRefs) : MatchResult;

/// <summary>
/// No matching queued job is available for this worker right now.
/// The worker should back off and retry after a short interval.
/// </summary>
public sealed record NoPendingJob : MatchResult;

/// <summary>
/// The global queue depth has reached or exceeded the configured maximum (1 000 jobs).
/// New intake via CreateJob should be rejected until the queue drains.
/// </summary>
public sealed record QueueFull : MatchResult;
#endif
