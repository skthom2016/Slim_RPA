#nullable enable
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;
using Tracto.Desktop.Actions;
using Tracto.Desktop.Protocol;

namespace Tracto.Desktop.Host;

/// <summary>
/// Fetches the signed artifact from the Orchestrator, verifies its hash,
/// deserialises the script, and executes each step via ActionDispatcher.
/// </summary>
internal sealed class ScriptExecutionEngine
{
    private readonly ActionDispatcher _dispatcher;
    private readonly HttpClient _http;
    private readonly ILogger<ScriptExecutionEngine> _log;

    // Case-insensitive to handle both PascalCase keys from LocalFilePublisher
    // and potential camelCase variants.
    private static readonly JsonSerializerOptions _jsonOpts =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    // Expands ${varName} placeholders in string parameter values using the shared run variables.
    private static Dictionary<string, object?> ExpandVars(
        Dictionary<string, object?> inputs,
        Dictionary<string, object?> vars)
    {
        var result = new Dictionary<string, object?>(inputs.Comparer);
        foreach (var (k, v) in inputs)
        {
            if (v is string s && s.Contains("${"))
            {
                var expanded = Regex.Replace(s, @"\$\{([^}]+)\}", m =>
                {
                    var name = m.Groups[1].Value;
                    return vars.TryGetValue(name, out var val) ? val?.ToString() ?? "" : m.Value;
                });
                result[k] = expanded;
            }
            else
            {
                result[k] = v;
            }
        }
        return result;
    }

    public ScriptExecutionEngine(
        ActionDispatcher dispatcher,
        HttpClient http,
        ILogger<ScriptExecutionEngine> log)
    {
        _dispatcher = dispatcher;
        _http       = http;
        _log        = log;
    }

    public async Task<(RunState State, List<StepResult> Steps)> RunAsync(
        JobContext job, CancellationToken ct)
    {
        var steps = new List<StepResult>();

        // ── 1. Fetch artifact ─────────────────────────────────────────────────
        HttpResponseMessage artResp;
        try
        {
            artResp = await _http.GetAsync(job.ArtifactUrl, ct);
            artResp.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to fetch artifact from {Url}", job.ArtifactUrl);
            return (RunState.Unknown, steps);
        }

        ArtifactGetResponse? artDto;
        try
        {
            artDto = await artResp.Content.ReadFromJsonAsync<ArtifactGetResponse>(_jsonOpts, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to deserialise artifact response");
            return (RunState.Unknown, steps);
        }

        if (artDto is null || string.IsNullOrEmpty(artDto.Payload))
        {
            _log.LogError("Artifact response has no payload");
            return (RunState.Unknown, steps);
        }

        // ── 2. Base64-decode and verify SHA-256 ───────────────────────────────
        byte[] raw;
        try { raw = Convert.FromBase64String(artDto.Payload); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Artifact payload is not valid base64");
            return (RunState.Failed, steps);
        }

        if (!string.IsNullOrEmpty(job.ArtifactDigest))
        {
            var computed = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
            // The digest stored in JobContext comes from the Orchestrator ETag (hex).
            var expected = job.ArtifactDigest.Trim('"').ToLowerInvariant();
            if (!computed.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogError("Artifact hash mismatch — expected={Expected} computed={Computed}",
                    expected, computed);
                return (RunState.Failed, steps);
            }
            _log.LogInformation("Artifact hash verified ✓");
        }

        // ── 3. Deserialise script ─────────────────────────────────────────────
        ScriptEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<ScriptEnvelope>(raw, _jsonOpts); }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to deserialise script envelope");
            return (RunState.Failed, steps);
        }

        if (envelope?.Script?.Steps is not { Count: > 0 })
        {
            _log.LogError("Script has no steps — envelope may be malformed");
            return (RunState.Failed, steps);
        }

        _log.LogInformation("Script ready: \"{Name}\" v{Ver} — {Count} step(s)",
            envelope.Script.Name, envelope.Script.Version, envelope.Script.Steps.Count);

        // ── 4. Execute steps in order ─────────────────────────────────────────
        // runVars is shared across all steps; built-ins are seeded at run start.
        var runVars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["now.iso"]   = DateTimeOffset.UtcNow.ToString("o"),
            ["now.local"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["now.date"]  = DateTimeOffset.Now.ToString("yyyy-MM-dd"),
            ["now.time"]  = DateTimeOffset.Now.ToString("HH:mm:ss"),
        };

        foreach (var step in envelope.Script.Steps)
        {
            var stepId    = $"step.{step.StepNumber}";
            var startedAt = DateTimeOffset.UtcNow;

            _log.LogInformation("[{StepId}] action={Action}  {Desc}",
                stepId, step.Action, step.Description ?? string.Empty);

            // Convert string-valued Parameters, then expand ${varName} placeholders.
            var rawInputs = (step.Parameters ?? new Dictionary<string, string?>())
                .ToDictionary(kv => kv.Key, kv => (object?)kv.Value,
                    StringComparer.OrdinalIgnoreCase);
            var inputs = ExpandVars(rawInputs, runVars);

            var ctx = new ActionContext
            {
                StepId            = stepId,
                Inputs            = inputs,
                Vars              = runVars,   // shared — handlers may write outputs here
                HttpClient        = _http,
                Logger            = _log,
                CancellationToken = ct,
            };

            ActionResult result;
            try
            {
                result = await _dispatcher.DispatchByActionAsync(step.Action, ctx);
            }
            catch (OperationCanceledException)
            {
                throw; // propagate so the caller records Cancelled
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[{StepId}] Unhandled exception in handler", stepId);
                result = ActionResult.Fail("EXECUTION_FAILED", ex.Message);
            }

            var completedAt = DateTimeOffset.UtcNow;
            var durationMs  = (long)(completedAt - startedAt).TotalMilliseconds;

            // Store handler outputs in runVars so later steps can reference them with ${varName}.
            // If the step declared storeAs="foo", outputs are keyed as "foo.body", "foo.statusCode", etc.
            if (result.Outputs is { Count: > 0 })
            {
                var ns = inputs.TryGetValue("storeAs", out var sa) && sa is string s && s.Length > 0
                    ? s + "."
                    : string.Empty;
                foreach (var (k, v) in result.Outputs)
                    runVars[ns + k] = v;
            }

            ErrorCategory? errCat = result.ErrorCategory switch
            {
                "TIMEOUT"          => ErrorCategory.Timeout,
                "CONFIGURATION"    => ErrorCategory.Validation,
                "NOT_FOUND"        => ErrorCategory.Execution,
                "EXECUTION_FAILED" => ErrorCategory.Execution,
                "POLICY_DENIED"    => ErrorCategory.PolicyDenied,
                null or ""         => null,
                _                  => ErrorCategory.Unknown,
            };

            steps.Add(new StepResult(
                StepId:        stepId,
                StepState:     result.Success ? StepState.Succeeded : StepState.Failed,
                StartedAt:     startedAt,
                CompletedAt:   completedAt,
                DurationMs:    durationMs,
                ErrorCategory: result.Success ? null : errCat,
                ErrorDetail:   result.ErrorDetail));

            if (!result.Success)
            {
                _log.LogError("[{StepId}] FAILED — {Cat}: {Detail}",
                    stepId, result.ErrorCategory, result.ErrorDetail);
                return (RunState.Failed, steps);
            }

            _log.LogInformation("[{StepId}] OK ({Ms}ms)", stepId, durationMs);
        }

        return (RunState.Succeeded, steps);
    }
}
