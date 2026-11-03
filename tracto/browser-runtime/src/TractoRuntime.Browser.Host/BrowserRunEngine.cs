using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using TractoRuntime.Browser.Actions;
using TractoRuntime.Browser.Assertion;
using TractoRuntime.Browser.Context;
using TractoRuntime.Browser.Contracts;
using TractoRuntime.Browser.Extraction;
using TractoRuntime.Browser.Failure;
using TractoRuntime.Browser.Locators;
using TractoRuntime.Browser.Session;

namespace TractoRuntime.Browser.Host;

public sealed record StepOutcome(string StepId, string Action, bool Succeeded, long DurationMs, string? Error);

public sealed record RunOutcome(
    string TerminalState,
    string? ErrorCategory,
    string? Message,
    IReadOnlyList<StepOutcome> Steps,
    IReadOnlyDictionary<string, string> Outputs)
{
    public string? UnknownReason => TerminalState == "Unknown" ? Message ?? "UnhandledHostException" : null;
}

/// <summary>
/// Runs one leased job: fetches the artifact, validates and translates the flat script,
/// opens an isolated browser context, executes the steps in order and closes the browser.
/// </summary>
public sealed partial class BrowserRunEngine(
    BrowserHostOptions options,
    IPlaywright playwright,
    OrchestratorClient orchestrator,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _log = loggerFactory.CreateLogger<BrowserRunEngine>();

    [GeneratedRegex(@"\$\{([^}]+)\}")]
    private static partial Regex VariablePattern();

    public async Task<RunOutcome> RunAsync(JobLease lease, CancellationToken ct)
    {
        var steps = new List<StepOutcome>();
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ── 1. Artifact ──────────────────────────────────────────────────────
        byte[] raw;
        try
        {
            raw = await orchestrator.GetArtifactAsync(lease, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Run {RunId}: could not fetch artifact {Id}/{Version}.",
                lease.RunId, lease.ArtifactId, lease.ArtifactVersion);
            return new RunOutcome("Unknown", "TRANSPORT", "ArtifactFetchFailed", steps, outputs);
        }

        if (!string.IsNullOrEmpty(lease.ArtifactHash))
        {
            var computed = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
            if (!computed.Equals(lease.ArtifactHash.Trim('"'), StringComparison.OrdinalIgnoreCase))
            {
                _log.LogError("Run {RunId}: artifact hash mismatch.", lease.RunId);
                return new RunOutcome("Failed", "VALIDATION", "Artifact hash mismatch.", steps, outputs);
            }
        }

        // ── 2. Parse and validate before any browser side effect ─────────────
        FlatScript script;
        List<(FlatStep Flat, string StepId)> plan;
        try
        {
            script = FlatScript.Parse(raw);

            if (!string.Equals(script.TargetRuntime, "browser", StringComparison.OrdinalIgnoreCase))
                throw new ScriptValidationException(
                    $"Script targetRuntime is '{script.TargetRuntime}'; this worker only runs 'browser' scripts.");

            if (script.Steps.Count == 0)
                throw new ScriptValidationException("Script has no steps.");

            plan = [];
            foreach (var flat in script.Steps)
            {
                var stepId = $"step.{flat.StepNumber}";
                StepTranslator.Translate(stepId, flat.Action, flat.Parameters, options.AllowCss);
                ResolveSecrets(stepId, flat.Parameters, validateOnly: true);
                plan.Add((flat, stepId));
            }
        }
        catch (ScriptValidationException ex)
        {
            _log.LogError("Run {RunId}: script rejected — {Message}", lease.RunId, ex.Message);
            return new RunOutcome("Failed", "VALIDATION", ex.Message, steps, outputs);
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Run {RunId}: artifact is not valid JSON.", lease.RunId);
            return new RunOutcome("Failed", "VALIDATION", "Artifact is not valid JSON.", steps, outputs);
        }

        // The script may set its own mode; otherwise the host's own setting applies. How long
        // the browser stays open after the last step is always the host's PauseAfterRunMs —
        // that is a background viewing convenience, not something a script declares.
        var mode = script.Mode ?? options.BrowserMode;

        _log.LogInformation("Run {RunId}: script \"{Name}\" v{Version} — {Count} step(s), mode={Mode} ({ModeSource}), channel={Channel}",
            lease.RunId, script.Name, script.Version, script.Steps.Count, mode,
            script.Mode is null ? "host setting" : "set by the script",
            string.IsNullOrEmpty(options.Channel) ? "bundled-chromium" : options.Channel);

        // ── 3. Browser ───────────────────────────────────────────────────────
        var factory = new PlaywrightBrowserFactory(playwright, options.Channel);
        var contextOptions = new BrowserContextManagerOptions
        {
            ViewportWidth  = options.ViewportWidth,
            ViewportHeight = options.ViewportHeight
        };
        var sessionStore = new SessionStore(
            Path.Combine(AppContext.BaseDirectory, "sessions"),
            loggerFactory.CreateLogger<SessionStore>());

        await using var contexts = new BrowserContextManager(
            factory, contextOptions, sessionStore, loggerFactory.CreateLogger<BrowserContextManager>());

        var resolver   = new WaitingSelectorStrategy(
            new LocatorResolver(), options.LocatorWaitMs, loggerFactory.CreateLogger<WaitingSelectorStrategy>());
        var classifier = new FailureClassifier(loggerFactory.CreateLogger<FailureClassifier>());
        var executor   = new ActionExecutor(
            resolver,
            classifier,
            loggerFactory.CreateLogger<ActionExecutor>(),
            domAssertionService:  new DomAssertionService(resolver, loggerFactory.CreateLogger<DomAssertionService>()),
            domExtractionService: new DomExtractionService(resolver, loggerFactory.CreateLogger<DomExtractionService>()));

        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in lease.Params)
            vars["param." + name] = value;
        if (lease.Params.Count > 0)
            _log.LogInformation("Run {RunId}: {Count} param(s): {Names}", lease.RunId, lease.Params.Count,
                string.Join(", ", lease.Params.Keys));

        var listStrategy = new SelectorStrategy(loggerFactory.CreateLogger<SelectorStrategy>());

        IPage page;
        try
        {
            var context = await contexts.AcquireContextAsync(lease.RunId, mode, ct).ConfigureAwait(false);
            page = await context.NewPageAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new RunOutcome("Cancelled", "CANCELLED", "Cancelled before the browser opened.", steps, outputs);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Run {RunId}: browser could not be launched.", lease.RunId);
            return new RunOutcome("Failed", "ENVIRONMENT", $"Browser launch failed: {ex.Message}", steps, outputs);
        }

        // ── 4. Steps ─────────────────────────────────────────────────────────
        string terminal = "Succeeded";
        string? category = null;
        string? message = null;

        try
        {
            foreach (var (flat, stepId) in plan)
            {
                ct.ThrowIfCancellationRequested();
                SeedBuiltIns(vars);

                var expanded = ExpandVariables(flat.Parameters, vars);
                ResolveSecrets(stepId, expanded, validateOnly: false);

                var translated = StepTranslator.Translate(stepId, flat.Action, expanded, options.AllowCss);
                var sw = Stopwatch.StartNew();

                _log.LogInformation("[{StepId}] {Action}  {Description}",
                    stepId, translated.Action, flat.Description ?? string.Empty);

                ActionResult result;
                if (translated.DelayMs is { } ms)
                {
                    await Task.Delay(ms, ct).ConfigureAwait(false);
                    result = new ActionResult { Succeeded = true };
                }
                else if (translated.Action == StepTranslator.GetTextsAction)
                {
                    result = await GetTextsAsync(page, translated, listStrategy, ct).ConfigureAwait(false);
                }
                else
                {
                    result = await executor.ExecuteAsync(page, translated.Browser!, ct).ConfigureAwait(false);
                }

                sw.Stop();

                if (!result.Succeeded && ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                steps.Add(new StepOutcome(stepId, translated.Action, result.Succeeded, sw.ElapsedMilliseconds,
                    result.Succeeded ? null : $"{result.ErrorCategory}/{result.DetailCode}: {result.ErrorMessage}"));

                if (!result.Succeeded)
                {
                    terminal = "Failed";
                    category = result.ErrorCategory?.ToString() ?? "INTERNAL";
                    message  = $"{stepId} ({translated.Action}) failed: {result.ErrorMessage} [{result.DetailCode}]";
                    _log.LogError("[{StepId}] FAILED {Category} {Detail}: {Message}",
                        stepId, result.ErrorCategory, result.DetailCode, result.ErrorMessage);
                    await CaptureFailureEvidenceAsync(page, lease.RunId, stepId).ConfigureAwait(false);
                    break;
                }

                StoreEvidence(translated.StoreAs, result.Evidence, vars, outputs);
                _log.LogInformation("[{StepId}] OK ({Ms} ms)", stepId, sw.ElapsedMilliseconds);
            }
        }
        catch (OperationCanceledException)
        {
            terminal = "Cancelled";
            category = "CANCELLED";
            message  = "Run cancelled.";
            _log.LogWarning("Run {RunId}: cancelled.", lease.RunId);
        }
        catch (Exception ex)
        {
            terminal = "Unknown";
            category = "INTERNAL";
            message  = $"UnhandledHostException: {ex.Message}";
            _log.LogError(ex, "Run {RunId}: unhandled error.", lease.RunId);
        }

        // ── 5. Close ─────────────────────────────────────────────────────────
        if (mode == BrowserMode.Headed && options.PauseAfterRunMs > 0 && terminal != "Cancelled")
        {
            _log.LogInformation("Run {RunId}: leaving the browser open for {Ms} ms before it closes.",
                lease.RunId, options.PauseAfterRunMs);
            try { await Task.Delay(options.PauseAfterRunMs, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception) { /* nothing to recover */ }
        }

        try
        {
            await contexts.ReleaseContextAsync(lease.RunId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Run {RunId}: context release reported an error.", lease.RunId);
        }

        return new RunOutcome(terminal, category, message, steps, outputs);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>getTexts</c>: the visible text of every element the target matches, joined with the
    /// step's separator, limited to the first <c>limit</c> when given. Outputs <c>value</c>
    /// and <c>count</c>.
    /// </summary>
    private static async Task<ActionResult> GetTextsAsync(
        IPage page, TranslatedStep step, SelectorStrategy strategy, CancellationToken ct)
    {
        var timeoutMs = (float)(step.Timeout?.TotalMilliseconds ?? 30_000);
        try
        {
            var locator = await strategy.ResolveAsync(page, step.ListSelector!, ct).ConfigureAwait(false);
            await locator.First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Attached, Timeout = timeoutMs
            }).ConfigureAwait(false);

            var texts = (await locator.AllInnerTextsAsync().ConfigureAwait(false))
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();
            if (step.ListLimit > 0 && texts.Count > step.ListLimit)
                texts = texts.Take(step.ListLimit).ToList();

            return new ActionResult
            {
                Succeeded = true,
                Evidence = new Dictionary<string, object?>
                {
                    ["value"] = string.Join(step.ListSeparator, texts),
                    ["count"] = texts.Count.ToString()
                }
            };
        }
        catch (TimeoutException ex)
        {
            return new ActionResult
            {
                Succeeded = false, ErrorCategory = ErrorCategory.TARGET_NOT_FOUND,
                DetailCode = "browser.locatorNotFound", ErrorMessage = ex.Message
            };
        }
        catch (PlaywrightException ex)
        {
            return new ActionResult
            {
                Succeeded = false, ErrorCategory = ErrorCategory.INTERNAL,
                DetailCode = "browser.getTextsFailed", ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// On a failed step, records the page URL and title and saves a screenshot under the
    /// evidence folder so the failure can be understood without re-running the script.
    /// </summary>
    private async Task CaptureFailureEvidenceAsync(IPage page, string runId, string stepId)
    {
        try
        {
            var title = await page.TitleAsync().ConfigureAwait(false);
            _log.LogError("[{StepId}] page at failure: url={Url} title=\"{Title}\"", stepId, page.Url, title);

            var dir = Path.Combine(AppContext.BaseDirectory, "evidence");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"{runId}-{stepId}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = file, FullPage = false }).ConfigureAwait(false);
            _log.LogError("[{StepId}] screenshot saved: {File}", stepId, file);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[{StepId}] could not capture failure evidence.", stepId);
        }
    }

    private static void SeedBuiltIns(Dictionary<string, string> vars)
    {
        var now = DateTimeOffset.Now;
        vars["now.iso"]   = now.ToUniversalTime().ToString("o");
        vars["now.local"] = now.ToString("yyyy-MM-dd HH:mm:ss");
        vars["now.date"]  = now.ToString("yyyy-MM-dd");
        vars["now.time"]  = now.ToString("HH:mm:ss");
    }

    private static Dictionary<string, string?> ExpandVariables(
        Dictionary<string, string?> parameters, Dictionary<string, string> vars)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in parameters)
        {
            if (v is null || !v.Contains("${", StringComparison.Ordinal))
            {
                result[k] = v;
                continue;
            }

            result[k] = VariablePattern().Replace(v, m =>
            {
                var name = m.Groups[1].Value;
                if (name.StartsWith("secret:", StringComparison.OrdinalIgnoreCase)) return m.Value;
                return vars.TryGetValue(name, out var val) ? val : m.Value;
            });
        }
        return result;
    }

    /// <summary>
    /// <c>${secret:NAME}</c> is read from the environment variable <c>TRACTO_SECRET_NAME</c>
    /// on the worker machine. The value is never logged or returned in results.
    /// </summary>
    private static void ResolveSecrets(string stepId, Dictionary<string, string?> parameters, bool validateOnly)
    {
        foreach (var key in parameters.Keys.ToList())
        {
            var v = parameters[key];
            if (v is null || !v.Contains("${secret:", StringComparison.OrdinalIgnoreCase)) continue;

            var replaced = Regex.Replace(v, @"\$\{secret:([A-Za-z0-9_]+)\}", m =>
            {
                var name = m.Groups[1].Value;
                var secret = System.Environment.GetEnvironmentVariable("TRACTO_SECRET_" + name.ToUpperInvariant());
                if (secret is null)
                    throw new ScriptValidationException(
                        $"{stepId}: secret '{name}' is not set. Define environment variable TRACTO_SECRET_{name.ToUpperInvariant()} on the worker.");
                return validateOnly ? m.Value : secret;
            }, RegexOptions.IgnoreCase);

            if (!validateOnly) parameters[key] = replaced;
        }
    }

    private static void StoreEvidence(
        string? storeAs,
        IReadOnlyDictionary<string, object?> evidence,
        Dictionary<string, string> vars,
        Dictionary<string, string> outputs)
    {
        if (evidence.Count == 0) return;

        foreach (var (k, v) in evidence)
        {
            var text = v switch
            {
                null => string.Empty,
                string s => s,
                IEnumerable<string> list => string.Join(",", list),
                _ => v.ToString() ?? string.Empty
            };

            var name = storeAs is null ? k : $"{storeAs}.{k}";
            vars[name] = text;
            if (storeAs is null) continue;

            outputs[name] = text;
            // The main value is also reachable by the plain storeAs name: ${first} and,
            // in the next flow step, ${param.first}.
            if (k == "value")
            {
                vars[storeAs] = text;
                outputs[storeAs] = text;
            }
        }
    }

    public static string BuildResultPayload(RunOutcome outcome)
    {
        var payload = new
        {
            steps   = outcome.Steps.Select(s => new { s.StepId, s.Action, ok = s.Succeeded, ms = s.DurationMs, s.Error }),
            outputs = outcome.Outputs
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}
