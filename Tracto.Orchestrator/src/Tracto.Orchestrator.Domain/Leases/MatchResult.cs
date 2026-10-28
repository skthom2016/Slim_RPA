#nullable enable
namespace Tracto.Orchestrator.Domain.Leases;

/// <summary>
/// Discriminated union for the outcome of a PullJob operation (OC-03).
/// Sealed class hierarchy — pattern matching is exhaustive.
/// </summary>
public abstract class MatchResult
{
    private protected MatchResult() { }

    /// <summary>
    /// A suitable queued job was found and a lease has been offered to the worker.
    /// The worker must call AcceptLease within the 30-second offer window.
    /// </summary>
    public sealed class JobOffered : MatchResult
    {
        public JobOffered(
            Guid     runId,
            Guid     attemptId,
            Guid     leaseId,
            string   artifactId,
            string   artifactVersion,
            string   artifactHash,
            string[] inputRefs,
            string[] secretRefs)
        {
            RunId           = runId;
            AttemptId       = attemptId;
            LeaseId         = leaseId;
            ArtifactId      = artifactId;
            ArtifactVersion = artifactVersion;
            ArtifactHash    = artifactHash;
            InputRefs       = inputRefs;
            SecretRefs      = secretRefs;
        }

        public Guid     RunId           { get; }
        public Guid     AttemptId       { get; }
        public Guid     LeaseId         { get; }
        public string   ArtifactId      { get; }
        public string   ArtifactVersion { get; }
        public string   ArtifactHash    { get; }
        public string[] InputRefs       { get; }
        public string[] SecretRefs      { get; }
    }

    /// <summary>No QUEUED job currently matches the worker's capabilities.</summary>
    public sealed class NoPendingJob : MatchResult { }

    /// <summary>
    /// The intake queue is at or above the configured depth limit.
    /// New intake via CreateJob should be rejected until the queue drains.
    /// </summary>
    public sealed class QueueFull : MatchResult { }
}
