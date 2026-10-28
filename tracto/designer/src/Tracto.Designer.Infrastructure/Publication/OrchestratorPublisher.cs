namespace Tracto.Designer.Infrastructure.Publication;

using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Tracto.Designer.Core.Publication;

public sealed class OrchestratorPublisher : IScriptPublisher
{
    private readonly HttpClient _http;
    private readonly PublicationGate _gate;
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    public OrchestratorPublisher(string orchestratorBaseUrl, PublicationGate gate)
    {
        _gate = gate;
        _http = new HttpClient { BaseAddress = new Uri(orchestratorBaseUrl.TrimEnd('/') + "/") };
    }

    public string Name => "Orchestrator Publisher";

    public async Task<PublicationResult> PublishAsync(PublicationRequest req, CancellationToken ct = default)
    {
        var gateResult = _gate.CheckGates(req);
        if (gateResult is not null) return gateResult;

        var s = req.Script;

        // Build a ScriptEnvelope that the Desktop Runtime can execute
        var envelope = new
        {
            schemaVersion = "1",
            script = new
            {
                id            = s.Id,
                name          = s.Name,
                version       = s.Version,
                targetRuntime = s.TargetRuntime,
                steps         = s.Steps.Select(st => new
                {
                    stepNumber  = st.StepNumber,
                    action      = st.Action,
                    description = st.Description,
                    condition   = st.Condition,
                    parameters  = st.Parameters
                })
            }
        };

        var envelopeJson = JsonSerializer.Serialize(envelope, _json);
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(envelopeJson));

        var body = new
        {
            schemaVersion = "1",
            payload,
            signature      = req.Signature.Digest
        };

        try
        {
            var url = $"artifacts/{Uri.EscapeDataString(s.Id)}/versions/{Uri.EscapeDataString(s.Version)}";
            var response = await _http.PutAsJsonAsync(url, body, _json, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return PublicationResult.Ok(s.Id);

            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return PublicationResult.Failure($"Orchestrator returned {(int)response.StatusCode}: {errorBody}");
        }
        catch (HttpRequestException ex)
        {
            return PublicationResult.Failure($"Cannot reach Orchestrator: {ex.Message}");
        }
    }
}
