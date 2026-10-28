#nullable enable

using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;
using Tracto.Contract.Models.Protocol;
using Tracto.Contract.Orchestrator;
using static Tracto.Contract.Models.ReconcileDisposition;

namespace Tracto.Desktop.Host;

/// <summary>
/// Adapts IOrchestratorServer to the Orchestrator's HTTP API.
/// Caches leaseId → {runId, attemptId} so subsequent calls can resolve the URL segment.
/// </summary>
internal sealed class HttpOrchestratorBridge : IOrchestratorServer
{
    private readonly HttpClient _http;
    private readonly string _runtimeType;
    private readonly string? _interactiveSessionId;
    private readonly ConcurrentDictionary<string, (string RunId, string AttemptId)> _leaseCache = new();

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public HttpOrchestratorBridge(HttpClient http, string runtimeType, string? interactiveSessionId)
    {
        _http = http;
        _runtimeType = runtimeType;
        _interactiveSessionId = interactiveSessionId;
    }

    // ── Worker registration ───────────────────────────────────────────────────

    public async Task<RegistrationResult> RegisterWorkerAsync(WorkerRegistration reg, CancellationToken ct = default)
    {
        var body = new
        {
            workerId                = reg.WorkerId,
            runtimeType             = reg.RuntimeType.ToString().ToLowerInvariant(),
            actionNames             = reg.AdvertisedCapabilities.ActionNamespaces,
            capabilityDigest        = reg.CapabilityDigest,
            absenceThresholdSeconds = 60
        };

        var resp = await _http.PostAsJsonAsync("/workers/register", body, JsonOpts, ct);

        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync(ct);
            return RegistrationResult.Refused("HTTP_ERROR", $"{(int)resp.StatusCode}: {detail}");
        }

        var dto = await resp.Content.ReadFromJsonAsync<RegisterWorkerDto>(JsonOpts, ct);
        return dto is not null
            ? RegistrationResult.Accepted(dto.WorkerId)
            : RegistrationResult.Refused("EMPTY_RESPONSE", "Orchestrator returned no body");
    }

    // ── Job pull ──────────────────────────────────────────────────────────────

    public async Task<PullJobResponse> PullJobAsync(PullJobRequest req, CancellationToken ct = default)
    {
        var body = new
        {
            workerId             = req.WorkerId,
            runtimeType          = _runtimeType,
            capabilityNames      = Array.Empty<string>(),
            interactiveSessionId = _interactiveSessionId
        };

        var resp = await _http.PostAsJsonAsync("/jobs/pull", body, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<PullResultDto>(JsonOpts, ct)
                  ?? throw new InvalidOperationException("Empty pull response");

        if (dto.Result == "JobOffered")
        {
            var lease = new LeaseGranted(
                LeaseId:          dto.LeaseId!,
                RunId:            dto.RunId!.ToString()!,
                AttemptId:        dto.AttemptId!.ToString()!,
                ArtifactUrl:      $"{_http.BaseAddress}artifacts/{dto.ArtifactId}/versions/{dto.ArtifactVersion}",
                ArtifactDigest:   dto.ArtifactHash ?? string.Empty,
                LeaseDurationMs:  60_000,
                JobSchemaVersion: "1");

            _leaseCache[lease.LeaseId] = (lease.RunId, lease.AttemptId);
            return new JobOffered(lease);
        }

        return new NoPendingJob(new NoJobAvailable(5_000));
    }

    // ── Lease accept ──────────────────────────────────────────────────────────

    public async Task AcceptLeaseAsync(LeaseAccepted accepted, CancellationToken ct = default)
    {
        if (!_leaseCache.TryGetValue(accepted.LeaseId, out var cached))
            throw new InvalidOperationException($"Unknown leaseId {accepted.LeaseId}");

        var body = new
        {
            attemptId = Guid.Parse(cached.AttemptId),
            workerId  = accepted.WorkerId
        };

        var resp = await _http.PostAsJsonAsync($"/jobs/{cached.RunId}/lease/accept", body, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
    }

    // ── Heartbeat ─────────────────────────────────────────────────────────────

    public async Task<HeartbeatAck> ProcessHeartbeatAsync(Heartbeat heartbeat, CancellationToken ct = default)
    {
        if (!_leaseCache.TryGetValue(heartbeat.LeaseId, out var cached))
            throw new InvalidOperationException($"Unknown leaseId {heartbeat.LeaseId}");

        var body = new
        {
            attemptId = Guid.Parse(cached.AttemptId),
            workerId  = heartbeat.WorkerId,
            seqNo     = (long)heartbeat.SequenceNumber
        };

        var resp = await _http.PostAsJsonAsync($"/jobs/{cached.RunId}/heartbeat", body, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        return new HeartbeatAck(heartbeat.LeaseId, heartbeat.SequenceNumber, false);
    }

    // ── Final result ──────────────────────────────────────────────────────────

    public async Task ProcessFinalResultAsync(FinalResult result, CancellationToken ct = default)
    {
        var run       = result.Result;
        var runIdStr  = run.RunId;
        var attemptId = Guid.Parse(run.AttemptId);

        var terminalState = run.RunState switch
        {
            RunState.Succeeded  => "Succeeded",
            RunState.Failed     => "Failed",
            RunState.Cancelled  => "Cancelled",
            RunState.TimedOut   => "TimedOut",
            RunState.Unknown    => "Unknown",
            RunState.HandedOff  => "HandedOff",
            _                   => "Unknown"
        };

        var body = new
        {
            attemptId,
            terminalState,
            errorCategory  = run.ErrorCategory?.ToString(),
            unknownReason  = run.UnknownReason,
            evidenceRefs   = (string[]?)null
        };

        var resp = await _http.PostAsJsonAsync($"/jobs/{runIdStr}/result", body, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        _leaseCache.TryRemove(result.LeaseId, out _);
    }

    // ── Reconcile ─────────────────────────────────────────────────────────────

    public async Task<ReconcileResponse> ReconcileAsync(ReconcileRequest req, CancellationToken ct = default)
    {
        if (!_leaseCache.TryGetValue(req.LeaseId, out var cached))
            throw new InvalidOperationException($"Unknown leaseId {req.LeaseId}");

        var body = new
        {
            leaseId          = req.LeaseId,
            workerId         = req.WorkerId,
            lastKnownStepId  = req.LastKnownStepId,
            lastKnownRunState = req.LastKnownRunState
        };

        var resp = await _http.PostAsJsonAsync($"/jobs/{cached.RunId}/reconcile", body, JsonOpts, ct);
        resp.EnsureSuccessStatusCode();
        return new ReconcileResponse(req.LeaseId, Unknown, null);
    }

    // ── Local DTOs for deserialisation ───────────────────────────────────────

    private sealed class RegisterWorkerDto
    {
        [JsonPropertyName("workerId")] public string WorkerId { get; set; } = string.Empty;
    }

    private sealed class PullResultDto
    {
        [JsonPropertyName("result")]         public string? Result         { get; set; }
        [JsonPropertyName("runId")]          public Guid?   RunId          { get; set; }
        [JsonPropertyName("attemptId")]      public Guid?   AttemptId      { get; set; }
        [JsonPropertyName("leaseId")]        public Guid?   LeaseIdGuid    { get; set; }
        [JsonPropertyName("artifactId")]     public string? ArtifactId     { get; set; }
        [JsonPropertyName("artifactVersion")] public string? ArtifactVersion { get; set; }
        [JsonPropertyName("artifactHash")]   public string? ArtifactHash   { get; set; }

        // Computed helper — not a JSON field.
        [JsonIgnore] public string? LeaseId => LeaseIdGuid?.ToString();
    }
}
