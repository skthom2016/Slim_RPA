#nullable enable
using System.Net.Http.Json;
using System.Text.Json;

namespace Tracto.Orchestrator.Integration.Tests.Shared;

/// <summary>
/// Convenience helpers for building and sending common API requests in ORCH-IT tests.
/// All methods are static and operate on a provided <see cref="HttpClient"/>.
/// </summary>
public static class ApiHelpers
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── Artifact ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Publishes an artifact version. Returns the response body.
    /// Throws if the status code is not 201.
    /// </summary>
    public static async Task<JsonElement> PublishArtifactAsync(
        HttpClient client,
        string artifactId = "art-1",
        string version = "1.0",
        string payload = "hello-world",
        string? signature = "sig-abc")
    {
        var body = new
        {
            schemaVersion = "1",
            payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload)),
            signature,
            selectorContent = (string?)null
        };
        var resp = await client.PutAsJsonAsync(
            $"/artifacts/{artifactId}/versions/{version}", body);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
    }

    // ── Workers ────────────────────────────────────────────────────────────────

    /// <summary>Registers a worker. Returns workerId from response.</summary>
    public static async Task<string> RegisterWorkerAsync(
        HttpClient client,
        string workerId,
        string runtimeType = "dotnet",
        string[]? actionNames = null,
        string? capabilityDigest = null,
        int absenceThresholdSeconds = 60)
    {
        var body = new
        {
            workerId,
            runtimeType,
            actionNames = actionNames ?? Array.Empty<string>(),
            capabilityDigest,
            absenceThresholdSeconds
        };
        var resp = await client.PostAsJsonAsync("/workers/register", body);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        return json.GetProperty("workerId").GetString()!;
    }

    // ── Jobs ───────────────────────────────────────────────────────────────────

    /// <summary>Creates a job. Returns the JSON response element.</summary>
    public static async Task<JsonElement> CreateJobAsync(
        HttpClient client,
        string artifactId = "art-1",
        string version = "1.0",
        string[]? inputRefs = null,
        string[]? secretRefs = null,
        string? eventId = null)
    {
        var body = new
        {
            scriptArtifactId = artifactId,
            scriptVersion = version,
            schemaVersion = "1",
            inputRefs = inputRefs ?? Array.Empty<string>(),
            secretRefs = secretRefs ?? Array.Empty<string>(),
            deadline = (DateTime?)null,
            eventId,
            triggerType = "Manual",
            businessJobId = (string?)null
        };
        var resp = await client.PostAsJsonAsync("/jobs", body);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
    }

    /// <summary>
    /// Worker pulls a job. Returns the JSON response element.
    /// Result field will be "JobOffered" or "NoPendingJob".
    /// </summary>
    public static async Task<JsonElement> PullJobAsync(
        HttpClient client,
        string workerId,
        string runtimeType = "dotnet",
        string[]? capabilityNames = null,
        string? interactiveSessionId = null)
    {
        var body = new
        {
            workerId,
            runtimeType,
            capabilityNames = capabilityNames ?? Array.Empty<string>(),
            interactiveSessionId
        };
        var resp = await client.PostAsJsonAsync("/jobs/pull", body);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
    }

    /// <summary>Accepts a lease offer. Returns JSON with runId, attemptId, artifactHash etc.</summary>
    public static async Task<JsonElement> AcceptLeaseAsync(
        HttpClient client,
        Guid runId,
        Guid attemptId,
        string workerId)
    {
        var body = new { attemptId, workerId };
        var resp = await client.PostAsJsonAsync($"/jobs/{runId}/lease/accept", body);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
    }

    /// <summary>Sends a heartbeat. Returns JSON with cancellationRequested field.</summary>
    public static async Task<JsonElement> SendHeartbeatAsync(
        HttpClient client,
        Guid runId,
        Guid attemptId,
        string workerId,
        long seqNo)
    {
        var body = new { attemptId, workerId, seqNo };
        var resp = await client.PostAsJsonAsync($"/jobs/{runId}/heartbeat", body);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
    }

    /// <summary>Commits a final result. Returns JSON response.</summary>
    public static async Task<HttpResponseMessage> CommitResultRawAsync(
        HttpClient client,
        Guid runId,
        Guid attemptId,
        string terminalState,
        string? unknownReason = null,
        string? payload = null)
    {
        var body = new
        {
            attemptId,
            terminalState,
            unknownReason,
            payload,
            errorCategory = (string?)null,
            businessOutcome = (string?)null,
            evidenceRefs = Array.Empty<string>()
        };
        return await client.PostAsJsonAsync($"/jobs/{runId}/result", body);
    }

    /// <summary>Commits a final result and asserts success.</summary>
    public static async Task<JsonElement> CommitResultAsync(
        HttpClient client,
        Guid runId,
        Guid attemptId,
        string terminalState,
        string? unknownReason = null,
        string? payload = null)
    {
        var resp = await CommitResultRawAsync(client, runId, attemptId, terminalState, unknownReason, payload);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
    }

    /// <summary>Sends a reconcile request. Returns the raw response.</summary>
    public static async Task<HttpResponseMessage> ReconcileAsync(
        HttpClient client,
        Guid runId,
        Guid attemptId,
        object[]? journalEntries = null)
    {
        var body = new
        {
            attemptId,
            journalEntries = journalEntries ?? Array.Empty<object>()
        };
        return await client.PostAsJsonAsync($"/jobs/{runId}/reconcile", body);
    }

    /// <summary>Gets a job summary. Returns JSON.</summary>
    public static async Task<JsonElement> GetJobAsync(HttpClient client, Guid runId)
    {
        var resp = await client.GetAsync($"/jobs/{runId}");
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
    }

    // ── Full run lifecycle helper ──────────────────────────────────────────────

    /// <summary>
    /// Executes the full lifecycle up to RUNNING state:
    /// publish artifact → register worker → create job → pull job → accept lease.
    /// Returns (runId, attemptId, workerId, artifactHash).
    /// </summary>
    public static async Task<(Guid runId, Guid attemptId, string workerId, string artifactHash)>
        RunToRunningAsync(
            HttpClient client,
            string workerIdValue = "worker-1",
            string runtimeType = "dotnet",
            string artifactId = "art-1",
            string artifactVersion = "1.0",
            string[]? inputRefs = null,
            string[]? secretRefs = null,
            string? interactiveSessionId = null)
    {
        // Publish artifact (ignore 409 — may already exist from prior call).
        var artResp = await client.PutAsJsonAsync(
            $"/artifacts/{artifactId}/versions/{artifactVersion}",
            new
            {
                schemaVersion = "1",
                payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("script-content")),
                signature = "sig-valid",
                selectorContent = (string?)null
            });
        // Allow 409 (version already exists) but not other failures.
        if (!artResp.IsSuccessStatusCode && (int)artResp.StatusCode != 409)
            artResp.EnsureSuccessStatusCode();

        await RegisterWorkerAsync(client, workerIdValue, runtimeType);

        var jobJson = await CreateJobAsync(client, artifactId, artifactVersion, inputRefs, secretRefs);
        var runId   = jobJson.GetProperty("runId").GetGuid();

        var pullJson    = await PullJobAsync(client, workerIdValue, runtimeType,
                                             interactiveSessionId: interactiveSessionId);
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();
        var attemptId    = pullJson.GetProperty("attemptId").GetGuid();

        var leaseJson    = await AcceptLeaseAsync(client, offeredRunId, attemptId, workerIdValue);
        var artifactHash = leaseJson.GetProperty("artifactHash").GetString()!;
        var leaseAttempt = leaseJson.GetProperty("attemptId").GetGuid();

        return (offeredRunId, leaseAttempt, workerIdValue, artifactHash);
    }

    // ── Admin / UNKNOWN resolution ─────────────────────────────────────────────

    /// <summary>Posts an operator resolution for an UNKNOWN run. Returns raw response.</summary>
    public static async Task<HttpResponseMessage> ResolveUnknownRunRawAsync(
        HttpClient client,
        Guid runId,
        string type,
        string justification = "Investigated and resolved.",
        string operatorId = "op-1")
    {
        var body = new { type, justification, operatorId };
        return await client.PostAsJsonAsync($"/admin/jobs/{runId}/resolve", body);
    }
}
