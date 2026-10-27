#nullable enable

using Tracto.Contract.Models.Protocol;

namespace Tracto.Contract.Orchestrator;

/// <summary>Discriminated union returned by the orchestrator in response to a pull-job request.</summary>
public abstract record PullJobResponse;

/// <summary>A job is available and the lease has been granted to the requesting worker.</summary>
public sealed record JobOffered(LeaseGranted Lease) : PullJobResponse;

/// <summary>No job is currently pending for the requesting worker.</summary>
public sealed record NoPendingJob(NoJobAvailable Message) : PullJobResponse;
