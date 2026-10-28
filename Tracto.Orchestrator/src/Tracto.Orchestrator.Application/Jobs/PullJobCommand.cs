#nullable enable
using Microsoft.Extensions.Logging;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Domain.Common;
using Tracto.Orchestrator.Domain.Journal;
using Tracto.Orchestrator.Domain.Leases;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Domain.Workers;

namespace Tracto.Orchestrator.Application.Jobs;

// ── Command ───────────────────────────────────────────────────────────────────

/// <summary>
/// A worker's request to pull the next matching job from the queue.
/// </summary>
/// <param name="WorkerId">Stable worker identifier.</param>
/// <param name="RuntimeType">Tag that must match the run's artifact RuntimeType.</param>
/// <param name="CapabilityNames">
/// Set of capability names the worker can satisfy.
/// The run's RequiredCapabilities must be a subset.
/// </param>
/// <param name="InteractiveSessionId">
/// Non-null for headed (interactive) workers.
/// Enforces that only one worker per session holds a foreground-input lease.
/// </param>
public sealed record PullJobCommand(
    string   WorkerId,
    string   RuntimeType,
    string[] CapabilityNames,
    string?  InteractiveSessionId = null);

// ── Handler ───────────────────────────────────────────────────────────────────

/// <summary>
/// Handles <see cref="PullJobCommand"/> inside a serializable DB transaction.
///
/// Algorithm (all steps inside a single atomic transaction):
///   1. Verify the worker exists and is AVAILABLE (not already holding a lease).
///   2. Find the first QUEUED run whose:
///      a) artifact RuntimeType matches <see cref="PullJobCommand.RuntimeType"/>.
///      b) required capabilities are a subset of <see cref="PullJobCommand.CapabilityNames"/>.
///      c) run has no current non-Expired LeaseRecord (not already offered).
///      d) if headed: no other non-Expired lease exists for the same InteractiveSessionId.
///   3. On match: create LeaseRecord (State=Offered, ExpiresAt=now+30s, AttemptId=new).
///      Pin artifact hash from ArtifactRecord.ContentHash.
///      RunRecord stays QUEUED — it moves to LEASED on AcceptLease.
///      Journal entry: LEASE_OFFERED.
///      Return JobOffered.
///   4. No match → NoPendingJob (no journal entry, no lease created).
/// </summary>
public sealed class PullJobCommandHandler
{
    /// <summary>Window within which a worker must call AcceptLease after receiving an offer.</summary>
    public static readonly TimeSpan LeaseOfferWindow = TimeSpan.FromSeconds(30);

    private readonly IRunRepository      _runRepo;
    private readonly IWorkerRepository   _workerRepo;
    private readonly IArtifactRepository _artifactRepo;
    private readonly ILeaseRepository    _leaseRepo;
    private readonly IJournalRepository  _journalRepo;
    private readonly IUnitOfWork         _unitOfWork;
    private readonly IClock              _clock;
    private readonly ILogger<PullJobCommandHandler> _logger;

    public PullJobCommandHandler(
        IRunRepository      runRepo,
        IWorkerRepository   workerRepo,
        IArtifactRepository artifactRepo,
        ILeaseRepository    leaseRepo,
        IJournalRepository  journalRepo,
        IUnitOfWork         unitOfWork,
        IClock              clock,
        ILogger<PullJobCommandHandler> logger)
    {
        _runRepo      = runRepo;
        _workerRepo   = workerRepo;
        _artifactRepo = artifactRepo;
        _leaseRepo    = leaseRepo;
        _journalRepo  = journalRepo;
        _unitOfWork   = unitOfWork;
        _clock        = clock;
        _logger       = logger;
    }

    public async Task<Result<MatchResult, Error>> HandleAsync(
        PullJobCommand    cmd,
        CancellationToken cancellationToken = default)
    {
        // ── Guard: basic input validation ──────────────────────────────────────
        if (string.IsNullOrWhiteSpace(cmd.WorkerId))
            return Result<MatchResult, Error>.Fail(
                Error.Validation("WorkerId is required."));

        if (string.IsNullOrWhiteSpace(cmd.RuntimeType))
            return Result<MatchResult, Error>.Fail(
                Error.Validation("RuntimeType is required."));

        await _unitOfWork.BeginAsync(cancellationToken);

        // committed tracks whether CommitAsync was reached. The finally block calls
        // RollbackAsync on any non-committed exit (early return OR exception) so the
        // underlying DB transaction is always released promptly (Domain Invariant 5).
        var committed = false;
        try
        {
            var now = _clock.UtcNow;

            // ── Step 1: worker must exist and be AVAILABLE ─────────────────────────
            var worker = await _workerRepo.GetByIdAsync(cmd.WorkerId, cancellationToken);
            if (worker is null)
                return Result<MatchResult, Error>.Fail(
                    Error.NotFound($"Worker '{cmd.WorkerId}' is not registered."));

            if (worker.State != WorkerState.Available)
            {
                _logger.LogDebug(
                    "PullJob: worker {WorkerId} is in state {State}; skipping dispatch.",
                    cmd.WorkerId, worker.State);
                return Result<MatchResult, Error>.Ok(new MatchResult.NoPendingJob());
            }

            // Worker must not already hold an active lease.
            var existingLease = await _leaseRepo.FindActiveForWorkerAsync(cmd.WorkerId, cancellationToken);
            if (existingLease is not null)
            {
                _logger.LogDebug(
                    "PullJob: worker {WorkerId} already holds active lease {LeaseId}; returning NoPendingJob.",
                    cmd.WorkerId, existingLease.LeaseId);
                return Result<MatchResult, Error>.Ok(new MatchResult.NoPendingJob());
            }

            // ── Step 2: scan QUEUED runs for a capability match ────────────────────
            var queuedRuns = await _runRepo.FindByStateAsync(RunState.Queued, cancellationToken);

            RunRecord? matchedRun  = null;
            string?    matchedHash = null;

            foreach (var run in queuedRuns)
            {
                if (string.IsNullOrWhiteSpace(run.ScriptArtifactId))
                    continue;

                var artifact = await _artifactRepo.GetRecordAsync(
                    run.ScriptArtifactId, run.ScriptVersion, cancellationToken);

                if (artifact is null || artifact.IsDeleted)
                    continue;

                // (a) RuntimeType must match artifact's declared runtime.
                if (!ArtifactRuntimeTypeMatches(artifact.SelectorContent, cmd.RuntimeType))
                    continue;

                // (b) Required capabilities must be a subset of worker capabilities.
                if (!RunCapabilitiesAreSatisfied(artifact.SelectorContent, cmd.CapabilityNames))
                    continue;

                // (c) Run must not already be offered to another worker.
                if (await _leaseRepo.HasActiveOfferForRunAsync(run.Id, cancellationToken))
                    continue;

                // (d) Headed-session exclusivity.
                if (!string.IsNullOrWhiteSpace(cmd.InteractiveSessionId))
                {
                    if (await _leaseRepo.HasActiveSessionLeaseAsync(cmd.InteractiveSessionId, cancellationToken))
                        continue;
                }
                else
                {
                    // Headless worker: skip jobs that require an interactive session.
                    if (RunRequiresInteractiveSession(artifact.SelectorContent))
                        continue;
                }

                matchedRun  = run;
                matchedHash = artifact.ContentHash;
                break;
            }

            if (matchedRun is null)
            {
                _logger.LogDebug(
                    "PullJob: no matching QUEUED run found for worker {WorkerId} (runtime={RuntimeType}).",
                    cmd.WorkerId, cmd.RuntimeType);
                return Result<MatchResult, Error>.Ok(new MatchResult.NoPendingJob());
            }

            // ── Step 3: create lease offer ─────────────────────────────────────────
            var leaseId   = Guid.NewGuid();
            var attemptId = Guid.NewGuid();
            var lease     = new LeaseRecord(
                leaseId:              leaseId,
                runId:                matchedRun.Id,
                attemptId:            attemptId,
                workerId:             cmd.WorkerId,
                artifactId:           matchedRun.ScriptArtifactId,
                artifactVersion:      matchedRun.ScriptVersion,
                artifactHash:         matchedHash!,
                offeredAt:            now,
                expiresAt:            now.Add(LeaseOfferWindow),
                interactiveSessionId: cmd.InteractiveSessionId);

            await _leaseRepo.AddAsync(lease, cancellationToken);

            await _journalRepo.AppendAsync(
                new JournalEntry(
                    runId:      matchedRun.Id,
                    attemptId:  attemptId,
                    eventType:  "LEASE_OFFERED",
                    occurredAt: now,
                    payload:    $"{{\"workerId\":\"{cmd.WorkerId}\",\"leaseId\":\"{leaseId}\"}}"),
                cancellationToken);

            await _unitOfWork.CommitAsync(cancellationToken);
            committed = true;

            _logger.LogInformation(
                "PullJob: offered run {RunId} (lease {LeaseId}, attempt {AttemptId}) to worker {WorkerId}.",
                matchedRun.Id, leaseId, attemptId, cmd.WorkerId);

            return Result<MatchResult, Error>.Ok(new MatchResult.JobOffered(
                runId:           matchedRun.Id,
                attemptId:       attemptId,
                leaseId:         leaseId,
                artifactId:      matchedRun.ScriptArtifactId,
                artifactVersion: matchedRun.ScriptVersion,
                artifactHash:    matchedHash!,
                inputRefs:       matchedRun.InputRefs,
                secretRefs:      matchedRun.SecretRefs));
        }
        finally
        {
            // RollbackAsync is documented as safe to call on an empty or already-committed
            // transaction; it is a no-op in those cases. Calling it here ensures that any
            // in-flight EF transaction is released immediately on all non-commit exit paths
            // (early NoPendingJob returns, error returns, or unexpected exceptions).
            if (!committed)
                await _unitOfWork.RollbackAsync();
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the artifact's SelectorContent JSON for the "runtimeType" field and
    /// compares it to the worker's declared RuntimeType.
    /// If the field is absent the artifact is treated as a wildcard (always matches).
    /// SelectorContent format: {"runtimeType":"dotnet","requiredCapabilities":["Foo"],"headed":false}
    /// </summary>
    private static bool ArtifactRuntimeTypeMatches(string? selectorContent, string workerRuntimeType)
    {
        if (string.IsNullOrWhiteSpace(selectorContent))
            return true;

        var runtimeType = ExtractStringField(selectorContent, "runtimeType");
        if (runtimeType is null)
            return true;

        return string.Equals(runtimeType, workerRuntimeType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks that all capability names listed in the artifact's SelectorContent
    /// "requiredCapabilities" array are present in <paramref name="workerCapabilities"/>.
    /// An absent or empty "requiredCapabilities" array is satisfied by any worker.
    /// </summary>
    private static bool RunCapabilitiesAreSatisfied(string? selectorContent, string[] workerCapabilities)
    {
        if (string.IsNullOrWhiteSpace(selectorContent))
            return true;

        var required = ExtractStringArray(selectorContent, "requiredCapabilities");
        if (required.Length == 0)
            return true;

        var workerSet = new HashSet<string>(workerCapabilities, StringComparer.OrdinalIgnoreCase);
        foreach (var cap in required)
        {
            if (!workerSet.Contains(cap))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns true when the artifact's SelectorContent marks the job as headed
    /// (requires an interactive session).
    /// </summary>
    private static bool RunRequiresInteractiveSession(string? selectorContent)
    {
        if (string.IsNullOrWhiteSpace(selectorContent))
            return false;

        var idx = selectorContent.IndexOf("\"headed\"", StringComparison.Ordinal);
        if (idx < 0) return false;

        var rest = selectorContent[(idx + 8)..].TrimStart();
        if (!rest.StartsWith(':')) return false;

        rest = rest[1..].TrimStart();
        return rest.StartsWith("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Minimal JSON string-field extractor (avoids taking a System.Text.Json dependency in Application).</summary>
    private static string? ExtractStringField(string json, string fieldName)
    {
        var key = $"\"{fieldName}\"";
        var idx = json.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return null;

        var after = json[(idx + key.Length)..].TrimStart();
        if (!after.StartsWith(':')) return null;

        after = after[1..].TrimStart();
        if (!after.StartsWith('"')) return null;

        after = after[1..];
        var end = after.IndexOf('"');
        return end < 0 ? null : after[..end];
    }

    /// <summary>Minimal JSON string-array extractor.</summary>
    private static string[] ExtractStringArray(string json, string fieldName)
    {
        var key = $"\"{fieldName}\"";
        var idx = json.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return [];

        var after = json[(idx + key.Length)..].TrimStart();
        if (!after.StartsWith(':')) return [];

        after = after[1..].TrimStart();
        if (!after.StartsWith('[')) return [];

        var end = after.IndexOf(']');
        if (end < 0) return [];

        var body = after[1..end];
        if (string.IsNullOrWhiteSpace(body)) return [];

        var parts  = body.Split(',');
        var result = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
                result.Add(trimmed[1..^1]);
        }
        return result.ToArray();
    }
}
