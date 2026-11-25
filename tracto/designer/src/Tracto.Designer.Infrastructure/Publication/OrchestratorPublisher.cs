namespace Tracto.Designer.Infrastructure.Publication;

using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Tracto.Designer.Core.Publication;
using Tracto.Designer.Infrastructure.Serialization;

public sealed class OrchestratorPublisher : IScriptPublisher
{
    private readonly HttpClient _http;
    private readonly PublicationGate _gate;
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    public OrchestratorPublisher(string orchestratorBaseUrl, PublicationGate gate)
        : this(new HttpClient { BaseAddress = new Uri(orchestratorBaseUrl.TrimEnd('/') + "/") }, gate)
    {
    }

    /// <summary>For callers that supply the HTTP client; its BaseAddress must end with a slash.</summary>
    public OrchestratorPublisher(HttpClient http, PublicationGate gate)
    {
        _gate = gate;
        _http = http;
    }

    public string Name => "Orchestrator Publisher";

    public async Task<PublicationResult> PublishAsync(PublicationRequest req, CancellationToken ct = default)
    {
        var gateResult = _gate.CheckGates(req);
        if (gateResult is not null) return gateResult;

        var s = req.Script;

        // Build the ScriptEnvelope the runtimes and the Orchestrator read. A segmented script
        // carries its segments; the Orchestrator queues one run per segment.
        var envelope = new
        {
            schemaVersion = "1",
            script = ScriptDocument.Build(s)
        };

        var envelopeJson = JsonSerializer.Serialize(envelope, _json);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(envelopeJson));

        var body = new
        {
            schemaVersion = "1",
            payload
        };

        try
        {
            var url = $"artifacts/{Uri.EscapeDataString(s.Id)}/versions/{Uri.EscapeDataString(s.Version)}";
            var response = await _http.PutAsJsonAsync(url, body, _json, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return PublicationResult.Failure($"Orchestrator returned {(int)response.StatusCode}: {errorBody}");
            }

            if (!req.SetAsLatest)
                return PublicationResult.Ok(s.Id);

            // The version is stored. Now move the tag to it. Publishing the same unchanged version
            // again is accepted by the Orchestrator, so this also promotes a version published earlier.
            var tagUrl = $"artifacts/{Uri.EscapeDataString(s.Id)}/aliases/latest";
            var tag = await _http.PutAsJsonAsync(tagUrl, new { version = s.Version, updatedBy = "designer" }, _json, ct)
                .ConfigureAwait(false);
            if (tag.IsSuccessStatusCode)
                return PublicationResult.Ok(s.Id, movedLatest: true);

            var tagError = await tag.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return PublicationResult.Failure(
                $"Version {s.Version} was published, but 'latest' could not be moved to it. " +
                $"Orchestrator returned {(int)tag.StatusCode}: {tagError}");
        }
        catch (HttpRequestException ex)
        {
            return PublicationResult.Failure($"Cannot reach Orchestrator: {ex.Message}");
        }
    }
}
