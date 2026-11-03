using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace TractoRuntime.Browser.Host;

/// <summary>One leased job as offered by the Orchestrator.</summary>
public sealed record JobLease(
    string RunId,
    string AttemptId,
    string LeaseId,
    string ArtifactId,
    string ArtifactVersion,
    string ArtifactHash);

/// <summary>Thin client for the Orchestrator's worker HTTP API.</summary>
public sealed class OrchestratorClient(HttpClient http, ILogger<OrchestratorClient> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RegisterAsync(string workerId, IReadOnlyList<string> actionNames, CancellationToken ct)
    {
        var body = new
        {
            workerId,
            runtimeType             = "browser",
            actionNames,
            absenceThresholdSeconds = 60
        };

        using var resp = await http.PostAsJsonAsync("workers/register", body, Json, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Registration refused: {(int)resp.StatusCode} {detail}");
        }
    }

    public async Task<JobLease?> PullAsync(string workerId, string? interactiveSessionId, CancellationToken ct)
    {
        var body = new
        {
            workerId,
            runtimeType     = "browser",
            capabilityNames = Array.Empty<string>(),
            interactiveSessionId
        };

        using var resp = await http.PostAsJsonAsync("jobs/pull", body, Json, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<PullDto>(Json, ct).ConfigureAwait(false);
        if (dto is null || !string.Equals(dto.Result, "JobOffered", StringComparison.Ordinal))
            return null;

        if (dto.RunId is null || dto.AttemptId is null || dto.LeaseId is null ||
            dto.ArtifactId is null || dto.ArtifactVersion is null)
        {
            logger.LogWarning("Pull returned JobOffered with missing fields; ignoring.");
            return null;
        }

        return new JobLease(dto.RunId, dto.AttemptId, dto.LeaseId,
            dto.ArtifactId, dto.ArtifactVersion, dto.ArtifactHash ?? string.Empty);
    }

    public async Task AcceptLeaseAsync(JobLease lease, string workerId, CancellationToken ct)
    {
        var body = new { attemptId = lease.AttemptId, workerId };
        using var resp = await http.PostAsJsonAsync($"jobs/{lease.RunId}/lease/accept", body, Json, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Sends one heartbeat. Returns <see langword="true"/> when the Orchestrator asks the run to stop.</summary>
    public async Task<bool> HeartbeatAsync(JobLease lease, string workerId, long seqNo, CancellationToken ct)
    {
        var body = new { attemptId = lease.AttemptId, workerId, seqNo };
        using var resp = await http.PostAsJsonAsync($"jobs/{lease.RunId}/heartbeat", body, Json, ct)
            .ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        try
        {
            using var doc = await JsonDocument.ParseAsync(
                await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct)
                .ConfigureAwait(false);
            return doc.RootElement.TryGetProperty("cancellationRequested", out var c) &&
                   c.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<byte[]> GetArtifactAsync(JobLease lease, CancellationToken ct)
    {
        using var resp = await http.GetAsync(
            $"artifacts/{lease.ArtifactId}/versions/{lease.ArtifactVersion}", ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var dto = await resp.Content.ReadFromJsonAsync<ArtifactDto>(Json, ct).ConfigureAwait(false);
        if (dto?.Payload is null || dto.Payload.Length == 0)
            throw new InvalidOperationException("Artifact response has no payload.");

        return Convert.FromBase64String(dto.Payload);
    }

    public async Task SubmitResultAsync(
        JobLease lease,
        string terminalState,
        string? errorCategory,
        string? unknownReason,
        string? payload,
        CancellationToken ct)
    {
        var body = new
        {
            attemptId     = lease.AttemptId,
            terminalState,
            errorCategory,
            unknownReason,
            evidenceRefs  = Array.Empty<string>(),
            payload
        };

        using var resp = await http.PostAsJsonAsync($"jobs/{lease.RunId}/result", body, Json, ct)
            .ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var detail = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new InvalidOperationException($"Result rejected: {(int)resp.StatusCode} {detail}");
        }
    }

    private sealed class PullDto
    {
        [JsonPropertyName("result")]          public string? Result          { get; set; }
        [JsonPropertyName("runId")]           public string? RunId           { get; set; }
        [JsonPropertyName("attemptId")]       public string? AttemptId       { get; set; }
        [JsonPropertyName("leaseId")]         public string? LeaseId         { get; set; }
        [JsonPropertyName("artifactId")]      public string? ArtifactId      { get; set; }
        [JsonPropertyName("artifactVersion")] public string? ArtifactVersion { get; set; }
        [JsonPropertyName("artifactHash")]    public string? ArtifactHash    { get; set; }
    }

    private sealed class ArtifactDto
    {
        [JsonPropertyName("payload")] public string? Payload { get; set; }
    }
}
