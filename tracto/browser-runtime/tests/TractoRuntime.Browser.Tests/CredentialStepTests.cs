using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using TractoRuntime.Browser.Diagnostics;
using TractoRuntime.Browser.Host;

namespace TractoRuntime.Browser.Tests;

/// <summary>
/// <c>${env:NAME}</c> in browser steps: which names a script reads, how values replace the
/// references, and that no secret value leaves the run in a log, an error message or an output.
/// </summary>
public sealed class CredentialStepTests : IDisposable
{
    private const string User = "fin.ops@acme.com";
    private const string Pass = "Zebra#Secret-4471";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tracto-cred-" + Guid.NewGuid().ToString("N"));

    public CredentialStepTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SecretMask.Clear();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private static IReadOnlyDictionary<string, string?> P(params (string Key, string Value)[] items) =>
        items.ToDictionary(i => i.Key, i => (string?)i.Value, StringComparer.OrdinalIgnoreCase);

    // ── names a script reads ─────────────────────────────────────────────────

    [Fact]
    public void Names_written_out_and_names_built_from_job_parameters_are_collected()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["param.site"] = "SITE1" };
        var names = RunSecrets.CollectNames(
        [
            ("step.1", P(("value", "${env:ACME_USERNAME}"))),
            ("step.2", P(("value", "${env:ACME_PASSWORD}"))),
            ("step.3", P(("value", "${env:${param.site}_PASSWORD}"), ("url", "${env:PORTAL_URL}/x"))),
            ("step.4", P(("value", "plain text"))),
        ], vars);

        names.Should().Equal("ACME_PASSWORD", "ACME_USERNAME", "PORTAL_URL", "SITE1_PASSWORD");
    }

    [Fact]
    public void A_name_built_from_an_earlier_steps_output_is_refused()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var act = () => RunSecrets.CollectNames(
            [("step.2", P(("value", "${env:${first.value}_PASSWORD}")))], vars);
        act.Should().Throw<ScriptValidationException>().WithMessage("*step.2*job parameters*");
    }

    [Fact]
    public void A_script_with_no_references_asks_for_nothing()
    {
        var names = RunSecrets.CollectNames(
            [("step.1", P(("value", "${param.x}"), ("url", "https://a.example/${now.date}")))],
            new Dictionary<string, string>());
        names.Should().BeEmpty();
    }

    // ── substituting ─────────────────────────────────────────────────────────

    private static RunSecrets Loaded(string password = Pass)
    {
        var s = new RunSecrets();
        s.Load(new Dictionary<string, EnvValue>
        {
            ["SITE_A_USERNAME"] = new(User, false),
            ["SITE_A_PASSWORD"] = new(password, true),
            ["SITE_A_CODE"] = new("123456", true),
            ["PORTAL_URL"] = new("https://portal.example.com", false),
        });
        return s;
    }

    [Fact]
    public void References_become_standins_then_the_real_values()
    {
        var secrets = Loaded();
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<string>();

        var protectedParams = secrets.Protect(P(
            ("a", "${env:SITE_A_USERNAME}"),
            ("b", "pw=${env:SITE_A_PASSWORD}!"),
            ("c", "${env:SITE_A_CODE}"),
            ("d", "${env:PORTAL_URL}/login")), vars, values);

        // while the rest is expanded, no real value and no ${...} from a value is visible
        foreach (var v in protectedParams.Values)
        {
            v.Should().NotContain(Pass).And.NotContain(User);
            v.Should().NotContain("${");
        }

        RunSecrets.Reveal(protectedParams, values);
        protectedParams["a"].Should().Be(User);
        protectedParams["b"].Should().Be("pw=" + Pass + "!");
        protectedParams["c"].Should().Be("123456");
        protectedParams["d"].Should().Be("https://portal.example.com/login");
    }

    [Fact]
    public void A_value_that_looks_like_a_variable_is_inserted_as_plain_text()
    {
        const string tricky = "P${now.date}x${secret:Y}!";
        var secrets = Loaded(tricky);
        var values = new List<string>();
        var p = secrets.Protect(P(("v", "${env:SITE_A_PASSWORD}")), new Dictionary<string, string>(), values);
        p["v"].Should().NotContain("${");
        RunSecrets.Reveal(p, values);
        p["v"].Should().Be(tricky);
    }

    [Fact]
    public void A_name_built_from_a_job_parameter_finds_its_variable()
    {
        var secrets = Loaded();
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["param.site"] = "SITE_A" };
        var values = new List<string>();
        var p = secrets.Protect(P(("v", "${env:${param.site}_USERNAME}")), vars, values);
        RunSecrets.Reveal(p, values);
        p["v"].Should().Be(User);
    }

    [Fact]
    public void A_variable_the_orchestrator_did_not_give_is_a_script_error()
    {
        var act = () => Loaded().Protect(P(("v", "${env:NOT_THERE}")), new Dictionary<string, string>(), []);
        act.Should().Throw<ScriptValidationException>().WithMessage("*NOT_THERE*not given*");
    }

    // ── the mask ─────────────────────────────────────────────────────────────

    [Fact]
    public void The_mask_hides_every_value_but_leaves_short_ones_and_other_text_alone()
    {
        SecretMask.Set([User, Pass, "123456", "ab"]);

        SecretMask.Apply($"typed {User} and {Pass} then 123456 ab").Should().Be("typed ●●●● and ●●●● then ●●●● ab");
        SecretMask.Apply("nothing to hide").Should().Be("nothing to hide");
        SecretMask.Apply(null).Should().BeEmpty();

        SecretMask.Clear();
        SecretMask.Apply(Pass).Should().Be(Pass);
    }

    [Fact]
    public void A_longer_value_is_masked_whole_before_a_shorter_one_inside_it()
    {
        SecretMask.Set(["Zebra", "Zebra#Secret-4471"]);
        SecretMask.Apply("x Zebra#Secret-4471 y").Should().Be("x ●●●● y");
    }

    [Fact]
    public void The_outcome_is_masked_in_message_step_errors_and_outputs()
    {
        SecretMask.Set([Pass]);
        var outcome = new RunOutcome("Failed", "VALIDATION", $"actual '{Pass}'",
            [new StepOutcome("step.1", "browser.page.assert", false, 5, $"saw {Pass}")],
            new Dictionary<string, string> { ["x"] = $"v={Pass}" });

        var masked = SecretMask.Mask(outcome);

        masked.Message.Should().Be("actual '●●●●'");
        masked.Steps[0].Error.Should().Be("saw ●●●●");
        masked.Outputs["x"].Should().Be("v=●●●●");
        BrowserRunEngine.BuildResultPayload(masked).Should().NotContain(Pass);
    }

    [Fact]
    public void Only_secret_values_are_masked_and_only_they_count_for_outputs()
    {
        var s = Loaded();
        s.HoldsSecret("the password is " + Pass).Should().BeTrue();
        s.HoldsSecret("code 123456 here").Should().BeTrue();
        s.HoldsSecret(User).Should().BeFalse("a plain value may be read from a page and kept");
        s.HoldsSecret("nothing").Should().BeFalse();
        s.HoldsSecret(null).Should().BeFalse();
        s.MaskValues().Should().BeEquivalentTo(Pass, "123456");
    }

    [Fact]
    public void The_console_line_is_masked_too()
    {
        SecretMask.Set([Pass]);
        var formatter = new MaskingConsoleFormatter();
        var writer = new StringWriter();
        var entry = new LogEntry<string>(LogLevel.Error, "Some.Category", new EventId(7), "failed on " + Pass, null, (s, _) => s);

        formatter.Write(entry, null, writer);

        var line = writer.ToString();
        line.Should().NotContain(Pass).And.Contain("failed on ●●●●").And.Contain("fail: Some.Category[7]");
    }

    // ── the whole run: a fake Orchestrator and a real headless browser ───────

    /// <summary>Answers the calls a run makes and records what it was asked.</summary>
    private sealed class FakeOrchestrator(string script) : HttpMessageHandler
    {
        public List<string[]> EnvRequests { get; } = [];
        public List<string> WorkerIds { get; } = [];
        public (HttpStatusCode Status, string Message)? Refusal { get; set; }
        public string Password { get; set; } = Pass;

        private (string Value, bool Secret) Resolve(string name) =>
            name.EndsWith("_PASSWORD", StringComparison.Ordinal) ? (Password, true)
            : name.EndsWith("_USERNAME", StringComparison.Ordinal) ? (User, false)
            : name == "LOGIN_TITLE" ? ("Sign in", false)
            : ("?", false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct);
            static HttpResponseMessage Json(HttpStatusCode code, object o) =>
                new(code) { Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json") };

            if (req.Method == HttpMethod.Get && path.Contains("/artifacts/"))
                return Json(HttpStatusCode.OK, new { payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(script)) });

            if (path.EndsWith("/env", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(body);
                WorkerIds.Add(doc.RootElement.GetProperty("workerId").GetString()!);
                var names = doc.RootElement.GetProperty("names").EnumerateArray().Select(n => n.GetString()!).ToArray();
                EnvRequests.Add(names);
                if (Refusal is { } r) return Json(r.Status, new { error = new { code = "VARIABLE_DENIED", message = r.Message } });
                return Json(HttpStatusCode.OK, new
                {
                    values = names.ToDictionary(n => n, n => new { value = Resolve(n).Value, secret = Resolve(n).Secret })
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private string WritePage()
    {
        var file = Path.Combine(_root, "login.html");
        File.WriteAllText(file, """
            <!doctype html><html><head><title>Sign in</title></head><body>
            <h1 id="title">Sign in</h1>
            <label>User ID <input id="user" aria-label="User ID"></label>
            <label>Password <input id="pw" type="password" aria-label="Password"></label>
            <button onclick="
              var u = document.getElementById('user').value, p = document.getElementById('pw').value;
              document.getElementById('msg').textContent =
                  (u === 'fin.ops@acme.com') ? ('Echo: ' + p) : 'Denied';
              document.getElementById('match').textContent = (p === 'P${now.date}x${param.site}!') ? 'MATCH' : 'NO';">Sign in</button>
            <p id="msg"></p><p id="match"></p>
            </body></html>
            """);
        return new Uri(file).AbsoluteUri;
    }

    private static string Script(string pageUrl, params string[] steps) =>
        "{\"id\":\"login-test\",\"name\":\"Login test\",\"version\":\"1.0.0\",\"targetRuntime\":\"browser\",\"steps\":[" +
        "{\"stepNumber\":1,\"action\":\"navigate\",\"parameters\":{\"url\":" + JsonSerializer.Serialize(pageUrl) + "}}," +
        string.Join(",", steps.Select((s, i) => "{\"stepNumber\":" + (i + 2) + "," + s + "}")) + "]}";

    private sealed record Ran(RunOutcome Outcome, FakeOrchestrator Fake, IReadOnlyList<RunLogEntry> Log, bool BrowserAvailable);

    private async Task<Ran> RunAsync(string script, Dictionary<string, string>? runParams = null,
        Action<FakeOrchestrator>? configure = null)
    {
        using var playwright = await Playwright.CreateAsync();
        try
        {
            await using var probe = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal))
        {
            return new Ran(new RunOutcome("Skipped", null, null, [], new Dictionary<string, string>()),
                new FakeOrchestrator(""), [], false);
        }

        var fake = new FakeOrchestrator(script);
        configure?.Invoke(fake);
        var http = new HttpClient(fake) { BaseAddress = new Uri("http://orchestrator.test/") };
        var client = new OrchestratorClient(http, NullLogger<OrchestratorClient>.Instance);

        var sink = new RunLogSink();
        using var loggers = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(new RunLogForwarder(sink)));
        var options = new BrowserHostOptions { Mode = "headless", FileRoots = _root, WorkerId = "w-test", PauseAfterRunMs = 0 };
        var engine = new BrowserRunEngine(options, playwright, client, loggers);

        var lease = new JobLease("run-1", "att-1", "lease-1", "login-test", "1.0.0", "", runParams ?? new Dictionary<string, string>());
        sink.BeginRun(lease.RunId, lease.AttemptId);
        var outcome = await engine.RunAsync(lease, CancellationToken.None);
        var entries = sink.EndRunAndDrain();
        return new Ran(SecretMask.Mask(outcome), fake, entries, true);
    }

    private static string Everything(Ran r) =>
        r.Outcome.Message + "\n" + string.Join("\n", r.Outcome.Steps.Select(s => s.Error)) + "\n" +
        string.Join("\n", r.Outcome.Outputs.Select(o => o.Key + "=" + o.Value)) + "\n" +
        string.Join("\n", r.Log.Select(l => l.Message)) + "\n" + BrowserRunEngine.BuildResultPayload(r.Outcome);

    [Fact]
    public async Task A_run_signs_in_with_stored_variables_and_no_secret_leaks()
    {
        var script = Script(WritePage(),
            "\"action\":\"fill\",\"parameters\":{\"label\":\"User ID\",\"value\":\"${env:ACME_USERNAME}\"}",
            "\"action\":\"fill\",\"parameters\":{\"label\":\"Password\",\"value\":\"${env:ACME_PASSWORD}\"}",
            "\"action\":\"click\",\"parameters\":{\"role\":\"button\",\"name\":\"Sign in\"}",
            "\"action\":\"assert\",\"parameters\":{\"kind\":\"textEquals\",\"css\":\"#msg\",\"expected\":\"Echo: " + Pass + "\"}",
            "\"action\":\"getText\",\"parameters\":{\"css\":\"#title\",\"storeAs\":\"title\"}");

        var r = await RunAsync(script);
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Succeeded", r.Outcome.Message);
        r.Outcome.Outputs["title"].Should().Be("Sign in");
        r.Fake.EnvRequests.Should().ContainSingle().Which.Should().Equal("ACME_PASSWORD", "ACME_USERNAME");
        r.Fake.WorkerIds.Should().Equal("w-test");
        Everything(r).Should().NotContain(Pass);
    }

    [Fact]
    public async Task A_variable_name_can_come_from_a_job_parameter_and_a_plain_value_is_read()
    {
        var script = Script(WritePage(),
            "\"action\":\"fill\",\"parameters\":{\"label\":\"User ID\",\"value\":\"${env:${param.site}_USERNAME}\"}",
            "\"action\":\"fill\",\"parameters\":{\"label\":\"Password\",\"value\":\"${env:${param.site}_PASSWORD}\"}",
            "\"action\":\"click\",\"parameters\":{\"role\":\"button\",\"name\":\"Sign in\"}",
            "\"action\":\"assert\",\"parameters\":{\"kind\":\"textEquals\",\"css\":\"#title\",\"expected\":\"${env:LOGIN_TITLE}\"}");

        var r = await RunAsync(script, new Dictionary<string, string> { ["site"] = "SITE1" });
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Succeeded", r.Outcome.Message);
        r.Fake.EnvRequests.Should().ContainSingle().Which.Should().Equal("LOGIN_TITLE", "SITE1_PASSWORD", "SITE1_USERNAME");
    }

    [Fact]
    public async Task A_plain_value_is_not_masked_and_may_be_kept_as_an_output()
    {
        var script = Script(WritePage(),
            "\"action\":\"fill\",\"parameters\":{\"label\":\"User ID\",\"value\":\"${env:ACME_USERNAME}\"}",
            "\"action\":\"getValue\",\"parameters\":{\"label\":\"User ID\",\"storeAs\":\"who\"}");

        var r = await RunAsync(script);
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Succeeded", r.Outcome.Message);
        r.Outcome.Outputs["who"].Should().Be(User);
    }

    [Fact]
    public async Task A_password_with_variable_syntax_reaches_the_page_as_typed()
    {
        const string tricky = "P${now.date}x${param.site}!";
        var script = Script(WritePage(),
            "\"action\":\"fill\",\"parameters\":{\"label\":\"User ID\",\"value\":\"${env:ACME_USERNAME}\"}",
            "\"action\":\"fill\",\"parameters\":{\"label\":\"Password\",\"value\":\"${env:ACME_PASSWORD}\"}",
            "\"action\":\"click\",\"parameters\":{\"role\":\"button\",\"name\":\"Sign in\"}",
            "\"action\":\"assert\",\"parameters\":{\"kind\":\"textEquals\",\"css\":\"#match\",\"expected\":\"MATCH\"}");

        // the page compares what it was typed with the literal text: variable syntax inside a
        // password must not have been expanded on the way
        var r = await RunAsync(script, new Dictionary<string, string> { ["site"] = "zzz" }, f => f.Password = tricky);
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Succeeded", r.Outcome.Message);
    }

    [Fact]
    public async Task A_step_that_would_store_a_secret_as_an_output_fails()
    {
        var script = Script(WritePage(),
            "\"action\":\"fill\",\"parameters\":{\"label\":\"User ID\",\"value\":\"${env:ACME_USERNAME}\"}",
            "\"action\":\"fill\",\"parameters\":{\"label\":\"Password\",\"value\":\"${env:ACME_PASSWORD}\"}",
            "\"action\":\"getValue\",\"parameters\":{\"label\":\"Password\",\"storeAs\":\"leaked\"}");

        var r = await RunAsync(script);
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Failed");
        r.Outcome.Message.Should().Contain("CREDENTIAL_IN_OUTPUT");
        r.Outcome.Outputs.Should().NotContainKey("leaked");
        Everything(r).Should().NotContain(Pass);
    }

    [Fact]
    public async Task A_failing_check_that_quotes_the_password_is_masked_everywhere()
    {
        // the page echoes the typed password, and the assertion's message quotes it
        var script = Script(WritePage(),
            "\"action\":\"fill\",\"parameters\":{\"label\":\"User ID\",\"value\":\"${env:ACME_USERNAME}\"}",
            "\"action\":\"fill\",\"parameters\":{\"label\":\"Password\",\"value\":\"${env:ACME_PASSWORD}\"}",
            "\"action\":\"click\",\"parameters\":{\"role\":\"button\",\"name\":\"Sign in\"}",
            "\"action\":\"assert\",\"parameters\":{\"kind\":\"textEquals\",\"css\":\"#msg\",\"expected\":\"Welcome\"}");

        var r = await RunAsync(script);
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Failed");
        r.Outcome.Message.Should().Contain("●●●●");
        Everything(r).Should().NotContain(Pass);
    }

    [Fact]
    public async Task A_refused_variable_fails_the_run_before_the_browser_opens()
    {
        var script = Script(WritePage(),
            "\"action\":\"fill\",\"parameters\":{\"label\":\"Password\",\"value\":\"${env:ACME_PASSWORD}\"}");

        var r = await RunAsync(script, configure: f =>
            f.Refusal = (HttpStatusCode.Forbidden, "Variable ACME_PASSWORD: the script may not read the variables of group acme"));
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Failed");
        r.Outcome.ErrorCategory.Should().Be("POLICY_DENIED");
        r.Outcome.Message.Should().Contain("ACME_PASSWORD").And.Contain("acme");
        r.Outcome.Steps.Should().BeEmpty("no step ran");
    }

    [Fact]
    public async Task A_script_without_references_never_calls_the_env_endpoint()
    {
        var script = Script(WritePage(),
            "\"action\":\"assert\",\"parameters\":{\"kind\":\"visible\",\"css\":\"#title\"}");

        var r = await RunAsync(script);
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Succeeded", r.Outcome.Message);
        r.Fake.EnvRequests.Should().BeEmpty();
    }
}
