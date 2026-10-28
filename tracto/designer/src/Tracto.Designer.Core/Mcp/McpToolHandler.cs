namespace Tracto.Designer.Core.Mcp;

using System.Text.Json;
using System.Text.Json.Serialization;
using Tracto.Designer.Core.Recording;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Validation;

#pragma warning disable CS9113
public sealed class McpToolHandler(
    IRecordingSessionService session,
    IScriptDraftGenerator draftGen,
    IScriptValidator validator)
#pragma warning restore CS9113
{
    private Script? _currentScript;

    // Session-level state: true only after validate_script returned valid=true (DS-08-AC05)
    private bool _sessionValidPassed = false;

    public void SetCurrentScript(Script? s)
    {
        _currentScript = s;
        _sessionValidPassed = false; // reset validation state when script changes
    }

    public McpToolResult HandleTool(string toolName, string? argsJson)
    {
        try
        {
            return toolName switch
            {
                "get_transcript"   => GetTranscript(),
                "get_capabilities" => GetCapabilities(argsJson),
                "get_draft_script" => GetDraftScript(),
                "validate_script"  => ValidateScript(argsJson),
                "update_script"    => UpdateScript(argsJson),
                _                  => new McpToolResult(false, string.Empty, $"Unknown tool: {toolName}")
            };
        }
        catch (Exception ex) { return new McpToolResult(false, string.Empty, ex.Message); }
    }

    private McpToolResult GetTranscript()
    {
        var entries = session.Transcript.Select(e => new
        {
            stepNumber  = e.StepNumber,
            timestamp   = e.Timestamp,
            description = e.StepDescription,
            eventType   = e.SourceEvent.EventType.ToString(),
            window      = e.SourceEvent.WindowTitle,
            narration   = e.NarrationText
        });
        return new McpToolResult(true, JsonSerializer.Serialize(entries));
    }

    // DS-08-AC03 / Appendix B: get_capabilities takes runtimeType and returns the capability
    // catalogue — {actionTypes[], predicateTypes[], policyConstraints} — for that runtime.
    private static McpToolResult GetCapabilities(string? argsJson)
    {
        string runtimeType = "desktop";
        if (!string.IsNullOrWhiteSpace(argsJson))
        {
            try
            {
                var doc = JsonDocument.Parse(argsJson);
                if (doc.RootElement.TryGetProperty("runtimeType", out var rt))
                    runtimeType = rt.GetString() ?? "desktop";
            }
            catch (JsonException) { /* use default */ }
        }

        var caps = runtimeType switch
        {
            "browser" => new
            {
                actionTypes = new[] { "click", "type", "scroll", "navigate", "findElement",
                                      "assertText", "wait", "fillForm", "selectOption",
                                      "captureScreenshot" },
                predicateTypes = new[] { "elementExists", "textMatches", "urlMatches",
                                         "elementVisible", "pageLoaded" },
                policyConstraints = new
                {
                    maxIterations     = 200,
                    forbiddenConstructs = new[] { "native_ui" }
                }
            },
            "terminal" => new
            {
                actionTypes = new[] { "command", "expect", "send", "wait", "capture",
                                      "assertOutput" },
                predicateTypes = new[] { "outputMatches", "processRunning", "fileExists" },
                policyConstraints = new
                {
                    maxIterations     = 100,
                    forbiddenConstructs = new[] { "gui_interaction" }
                }
            },
            _ => new // desktop (default)
            {
                actionTypes = new[] { "click", "type", "scroll", "drag", "keypress",
                                      "screenshot", "wait", "navigate", "findElement",
                                      "assertText" },
                predicateTypes = new[] { "elementExists", "textMatches", "windowOpen",
                                         "processRunning", "fileExists" },
                policyConstraints = new
                {
                    maxIterations     = 100,
                    forbiddenConstructs = Array.Empty<string>()
                }
            }
        };

        return new McpToolResult(true, JsonSerializer.Serialize(caps));
    }

    private McpToolResult GetDraftScript()
    {
        if (_currentScript is null)
            return new McpToolResult(false, string.Empty, "No script loaded. Record a session and generate a draft first.");
        var envelope = new
        {
            id            = _currentScript.Id,
            name          = _currentScript.Name,
            version       = _currentScript.Version,
            targetRuntime = _currentScript.TargetRuntime,
            steps         = _currentScript.Steps.Select(s => new
            {
                stepNumber  = s.StepNumber,
                description = s.Description,
                action      = s.Action,
                parameters  = s.Parameters,
                condition   = s.Condition
            })
        };
        return new McpToolResult(true, JsonSerializer.Serialize(envelope));
    }

    // DS-08 / Appendix B: validate_script accepts an optional candidateScript JSON string
    // so Claude Code can validate a candidate before committing it.
    // Response shape: {valid: bool, violations: [{severity, stepId, fieldPath, violationType,
    //                                              message, suggestedFix}]}
    private McpToolResult ValidateScript(string? argsJson)
    {
        Script scriptToValidate;

        if (!string.IsNullOrWhiteSpace(argsJson))
        {
            try
            {
                var doc = JsonDocument.Parse(argsJson);
                if (doc.RootElement.TryGetProperty("candidateScript", out var cs))
                {
                    var candidateJson = cs.GetString()
                        ?? throw new ArgumentException("candidateScript must be a JSON string.");
                    scriptToValidate = DeserializeCandidateScript(candidateJson);
                }
                else
                {
                    if (_currentScript is null)
                        return new McpToolResult(false, string.Empty, "No script loaded.");
                    scriptToValidate = _currentScript;
                }
            }
            catch (JsonException ex)
            {
                return new McpToolResult(false, string.Empty, $"Invalid JSON: {ex.Message}");
            }
        }
        else
        {
            if (_currentScript is null)
                return new McpToolResult(false, string.Empty, "No script loaded.");
            scriptToValidate = _currentScript;
        }

        var result = validator.Validate(scriptToValidate);

        // Track session validation state: flag is true only when the validated script is valid.
        _sessionValidPassed = result.IsValid;

        var output = new
        {
            valid      = result.IsValid,
            violations = result.Errors.Select(e => new
            {
                severity      = e.Severity.ToString().ToLowerInvariant(),
                stepId        = e.StepNumber?.ToString(),
                fieldPath     = FieldPath(e.RuleId, e.StepNumber),
                violationType = e.RuleId,
                message       = e.Message,
                suggestedFix  = SuggestedFix(e.RuleId)
            })
        };
        return new McpToolResult(true, JsonSerializer.Serialize(output));
    }

    // DS-08-AC05: update_script only accepts scripts that passed validate_script in the same
    // session. An unapproved or invalid patch is rejected.
    private McpToolResult UpdateScript(string? argsJson)
    {
        if (_currentScript is null) return new McpToolResult(false, string.Empty, "No script loaded.");

        if (!_sessionValidPassed)
            return new McpToolResult(false, string.Empty,
                "validate_script must be called and return valid=true before update_script can be applied.");

        if (string.IsNullOrWhiteSpace(argsJson))
            return new McpToolResult(false, string.Empty, "args required: { approved: true, steps: [...] }");
        try
        {
            var doc     = JsonDocument.Parse(argsJson);
            bool approved = doc.RootElement.TryGetProperty("approved", out var a) && a.GetBoolean();
            if (!approved)
                return new McpToolResult(false, string.Empty, "Author approval required. Set approved=true to apply update.");

            if (doc.RootElement.TryGetProperty("steps", out var stepsEl))
            {
                foreach (var stepPatch in stepsEl.EnumerateArray())
                {
                    int n     = stepPatch.GetProperty("stepNumber").GetInt32();
                    string? desc = stepPatch.TryGetProperty("description", out var d) ? d.GetString() : null;
                    var existing  = _currentScript.Steps.FirstOrDefault(s => s.StepNumber == n);
                    if (existing is not null && desc is not null)
                        existing.Description = desc;
                }
            }
            return new McpToolResult(true, JsonSerializer.Serialize(new { applied = true, stepCount = _currentScript.Steps.Count }));
        }
        catch (JsonException ex) { return new McpToolResult(false, string.Empty, $"Invalid JSON: {ex.Message}"); }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Script DeserializeCandidateScript(string json)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var dto  = JsonSerializer.Deserialize<CandidateScriptDto>(json, opts)
                   ?? throw new ArgumentException("candidateScript could not be parsed.");

        var script = new Script
        {
            Id            = dto.Id ?? string.Empty,
            Name          = dto.Name ?? string.Empty,
            Version       = dto.Version ?? string.Empty,
            TargetRuntime = dto.TargetRuntime ?? string.Empty
        };
        if (dto.Steps is not null)
        {
            foreach (var step in dto.Steps)
                script.Steps.Add(new ScriptStep
                {
                    StepNumber  = step.StepNumber,
                    Description = step.Description ?? string.Empty,
                    Action      = step.Action ?? string.Empty,
                    Condition   = step.Condition,
                    Parameters  = step.Parameters ?? []
                });
        }
        return script;
    }

    private static string FieldPath(string ruleId, int? stepNumber)
    {
        int idx = (stepNumber ?? 1) - 1;
        return ruleId switch
        {
            "VAL-001" => "id",
            "VAL-002" => "name",
            "VAL-003" => "version",
            "VAL-004" => "targetRuntime",
            "VAL-005" => "steps",
            "VAL-010" => $"steps[{idx}].stepNumber",
            "VAL-011" => $"steps[{idx}].description",
            "VAL-012" => $"steps[{idx}].action",
            _          => string.Empty
        };
    }

    private static string SuggestedFix(string ruleId) => ruleId switch
    {
        "VAL-001" => "Provide a unique identifier for this script.",
        "VAL-002" => "Provide a descriptive name for this script.",
        "VAL-003" => "Use semantic versioning format (e.g., 1.0.0).",
        "VAL-004" => "Set targetRuntime to 'desktop', 'browser', or 'terminal'.",
        "VAL-005" => "Add at least one step before publishing.",
        "VAL-010" => "Renumber steps sequentially starting from 1.",
        "VAL-011" => "Add a description for this step.",
        "VAL-012" => "Specify an action type for this step.",
        _          => string.Empty
    };

    // ---------------------------------------------------------------------------
    // DTOs for candidateScript deserialization
    // ---------------------------------------------------------------------------

    private sealed record CandidateScriptDto(
        [property: JsonPropertyName("id")]            string? Id,
        [property: JsonPropertyName("name")]          string? Name,
        [property: JsonPropertyName("version")]       string? Version,
        [property: JsonPropertyName("targetRuntime")] string? TargetRuntime,
        [property: JsonPropertyName("steps")]         List<CandidateStepDto>? Steps);

    private sealed record CandidateStepDto(
        [property: JsonPropertyName("stepNumber")]  int StepNumber,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("action")]      string? Action,
        [property: JsonPropertyName("condition")]   string? Condition,
        [property: JsonPropertyName("parameters")]  Dictionary<string, string?>? Parameters);
}
