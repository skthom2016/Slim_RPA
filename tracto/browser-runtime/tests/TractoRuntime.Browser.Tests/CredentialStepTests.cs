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
/// <c>${credential:NAME.field}</c> and <c>${setting:NAME}</c> in browser steps: which names a script
/// reads, how values replace the references, and that no credential value leaves the run in a log,
/// an error message or an output.
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
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["param.site"] = "bank.hdfc" };
        var (creds, settings) = RunSecrets.CollectNames(
        [
            ("step.1", P(("value", "${credential:givewell-portal.username}"))),
            ("step.2", P(("value", "${credential:givewell-portal.password}"))),
            ("step.3", P(("value", "${credential:${param.site}.password}"), ("url", "${setting:portal-url}/x"))),
            ("step.4", P(("value", "plain text"))),
        ], vars);

        creds.Should().Equal("bank.hdfc", "givewell-portal");
        settings.Should().Equal("portal-url");
    }

    [Fact]
    public void A_name_built_from_an_earlier_steps_output_is_refused()
    {
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var act = () => RunSecrets.CollectNames(
            [("step.2", P(("value", "${credential:${first.value}.password}")))], vars);
        act.Should().Throw<ScriptValidationException>().WithMessage("*step.2*job parameters*");
    }

    [Fact]
    public void A_script_with_no_references_asks_for_nothing()
    {
        var (creds, settings) = RunSecrets.CollectNames(
            [("step.1", P(("value", "${param.x}"), ("url", "https://a.example/${now.date}")))],
            new Dictionary<string, string>());
        creds.Should().BeEmpty();
        settings.Should().BeEmpty();
    }

    // ── substituting ─────────────────────────────────────────────────────────

    private static RunSecrets Loaded(string password = Pass)
    {
        var s = new RunSecrets();
        s.Load(new Dictionary<string, CredentialValues> { ["site-a"] = new(User, password, "123456") },
               new Dictionary<string, string> { ["portal-url"] = "https://portal.example.com" });
        return s;
    }

    [Fact]
    public void References_become_standins_then_the_real_values()
    {
        var secrets = Loaded();
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var values = new List<string>();

        var protectedParams = secrets.Protect(P(
            ("a", "${credential:site-a.username}"),
            ("b", "pw=${credential:site-a.password}!"),
            ("c", "${credential:site-a.totp}"),
            ("d", "${setting:portal-url}/login")), vars, values);

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
        var p = secrets.Protect(P(("v", "${credential:site-a.password}")), new Dictionary<string, string>(), values);
        p["v"].Should().NotContain("${");
        RunSecrets.Reveal(p, values);
        p["v"].Should().Be(tricky);
    }

    [Fact]
    public void A_name_built_from_a_job_parameter_finds_its_credential()
    {
        var secrets = Loaded();
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["param.site"] = "site-a" };
        var values = new List<string>();
        var p = secrets.Protect(P(("v", "${credential:${param.site}.username}")), vars, values);
        RunSecrets.Reveal(p, values);
        p["v"].Should().Be(User);
    }

    [Fact]
    public void A_one_time_code_is_refused_when_the_credential_has_no_key()
    {
        var s = new RunSecrets();
        s.Load(new Dictionary<string, CredentialValues> { ["site-a"] = new(User, Pass, null) }, new Dictionary<string, string>());
        var act = () => s.Protect(P(("v", "${credential:site-a.totp}")), new Dictionary<string, string>(), []);
        act.Should().Throw<ScriptValidationException>().WithMessage("*no one-time-code key*");
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
    public void Only_passwords_and_codes_count_as_secrets_for_outputs()
    {
        var s = Loaded();
        s.HoldsSecret("the password is " + Pass).Should().BeTrue();
        s.HoldsSecret("code 123456 here").Should().BeTrue();
        s.HoldsSecret(User).Should().BeFalse("a username may be read from a page and kept");
        s.HoldsSecret("nothing").Should().BeFalse();
        s.HoldsSecret(null).Should().BeFalse();
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

    /// <summary>Answers the three calls a run makes and records what it was asked.</summary>
    private sealed class FakeOrchestrator(string script) : HttpMessageHandler
    {
        public List<string[]> CredentialRequests { get; } = [];
        public List<string> WorkerIds { get; } = [];
        public int SettingsRequests { get; private set; }
        public (HttpStatusCode Status, string Message)? Refusal { get; set; }
        public string Password { get; set; } = Pass;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync(ct);
            static HttpResponseMessage Json(HttpStatusCode code, object o) =>
                new(code) { Content = new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json") };

            if (req.Method == HttpMethod.Get && path.Contains("/artifacts/"))
                return Json(HttpStatusCode.OK, new { payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(script)) });

            if (path.EndsWith("/credentials", StringComparison.Ordinal))
            {
                using var doc = JsonDocument.Parse(body);
                WorkerIds.Add(doc.RootElement.GetProperty("workerId").GetString()!);
                var names = doc.RootElement.GetProperty("names").EnumerateArray().Select(n => n.GetString()!).ToArray();
                CredentialRequests.Add(names);
                if (Refusal is { } r) return Json(r.Status, new { error = "CREDENTIAL_DENIED", message = r.Message });
                return Json(HttpStatusCode.OK, new
                {
                    credentials = names.ToDictionary(n => n, _ => new { username = User, password = Password })
                });
            }

            if (path.EndsWith("/settings", StringComparison.Ordinal))
            {
                SettingsRequests++;
                return Json(HttpStatusCode.OK, new { settings = new Dictionary<string, string> { ["login-title"] = "Sign in" } });
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
    public async Task A_run_signs_in_with_a_stored_credential_and_nothing_leaks()
    {
        var script = Script(WritePage(),
            "\"action\":\"fill\",\"parameters\":{\"label\":\"User ID\",\"value\":\"${credential:site-a.username}\"}",
            "\"action\":\"fill\",\"parameters\":{\"label\":\"Password\",\"value\":\"${credential:site-a.password}\"}",
            "\"action\":\"click\",\"parameters\":{\"role\":\"button\",\"name\":\"Sign in\"}",
            "\"action\":\"assert\",\"parameters\":{\"kind\":\"textEquals\",\"css\":\"#msg\",\"expected\":\"Echo: " + Pass + "\"}",
            "\"action\":\"getText\",\"parameters\":{\"css\":\"#title\",\"storeAs\":\"title\"}");

        var r = await RunAsync(script);
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Succeeded", r.Outcome.Message);
        r.Outcome.Outputs["title"].Should().Be("Sign in");
        r.Fake.CredentialRequests.Should().ContainSingle().Which.Should().Equal("site-a");
        r.Fake.WorkerIds.Should().Equal("w-test");
        Everything(r).Should().NotContain(Pass).And.NotContain(User);
    }

    [Fact]
    public async Task A_credential_name_can_come_from_a_job_parameter_and_a_setting_is_read()
    {
        var script = Script(WritePage(),
            "\"action\":\"fill\",\"parameters\":{\"label\":\"User ID\",\"value\":\"${credential:${param.site}.username}\"}",
            "\"action\":\"fill\",\"parameters\":{\"label\":\"Password\",\"value\":\"${credential:${param.site}.password}\"}",
            "\"action\":\"click\",\"parameters\":{\"role\":\"button\",\"name\":\"Sign in\"}",
            "\"action\":\"assert\",\"parameters\":{\"kind\":\"textEquals\",\"css\":\"#title\",\"expected\":\"${setting:login-title}\"}");

        var r = await RunAsync(script, new Dictionary<string, string> { ["site"] = "bank.hdfc" });
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Succeeded", r.Outcome.Message);
        r.Fake.CredentialRequests.Should().ContainSingle().Which.Should().Equal("bank.hdfc");
        r.Fake.SettingsRequests.Should().Be(1);
    }

    [Fact]
    public async Task A_password_with_variable_syntax_reaches_the_page_as_typed()
    {
        const string tricky = "P${now.date}x${param.site}!";
        var script = Script(WritePage(),
            "\"action\":\"fill\",\"parameters\":{\"label\":\"User ID\",\"value\":\"${credential:site-a.username}\"}",
            "\"action\":\"fill\",\"parameters\":{\"label\":\"Password\",\"value\":\"${credential:site-a.password}\"}",
            "\"action\":\"click\",\"parameters\":{\"role\":\"button\",\"name\":\"Sign in\"}",
            "\"action\":\"assert\",\"parameters\":{\"kind\":\"textEquals\",\"css\":\"#match\",\"expected\":\"MATCH\"}");

        // the page compares what it was typed with the literal text: variable syntax inside a
        // password must not have been expanded on the way
        var r = await RunAsync(script, new Dictionary<string, string> { ["site"] = "zzz" }, f => f.Password = tricky);
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Succeeded", r.Outcome.Message);
    }

    [Fact]
    public async Task A_step_that_would_store_a_password_as_an_output_fails()
    {
        var script = Script(WritePage(),
            "\"action\":\"fill\",\"parameters\":{\"label\":\"User ID\",\"value\":\"${credential:site-a.username}\"}",
            "\"action\":\"fill\",\"parameters\":{\"label\":\"Password\",\"value\":\"${credential:site-a.password}\"}",
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
            "\"action\":\"fill\",\"parameters\":{\"label\":\"User ID\",\"value\":\"${credential:site-a.username}\"}",
            "\"action\":\"fill\",\"parameters\":{\"label\":\"Password\",\"value\":\"${credential:site-a.password}\"}",
            "\"action\":\"click\",\"parameters\":{\"role\":\"button\",\"name\":\"Sign in\"}",
            "\"action\":\"assert\",\"parameters\":{\"kind\":\"textEquals\",\"css\":\"#msg\",\"expected\":\"Welcome\"}");

        var r = await RunAsync(script);
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Failed");
        r.Outcome.Message.Should().Contain("●●●●");
        Everything(r).Should().NotContain(Pass);
    }

    [Fact]
    public async Task A_refused_credential_fails_the_run_before_the_browser_opens()
    {
        var script = Script(WritePage(),
            "\"action\":\"fill\",\"parameters\":{\"label\":\"Password\",\"value\":\"${credential:site-a.password}\"}");

        var r = await RunAsync(script, configure: f =>
            f.Refusal = (HttpStatusCode.Forbidden, "Credential site-a: the script may not read the credentials of group finance"));
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Failed");
        r.Outcome.ErrorCategory.Should().Be("POLICY_DENIED");
        r.Outcome.Message.Should().Contain("site-a").And.Contain("finance");
        r.Outcome.Steps.Should().BeEmpty("no step ran");
    }

    [Fact]
    public async Task A_script_without_references_never_calls_the_credential_endpoints()
    {
        var script = Script(WritePage(),
            "\"action\":\"assert\",\"parameters\":{\"kind\":\"visible\",\"css\":\"#title\"}");

        var r = await RunAsync(script);
        if (!r.BrowserAvailable) return;

        r.Outcome.TerminalState.Should().Be("Succeeded", r.Outcome.Message);
        r.Fake.CredentialRequests.Should().BeEmpty();
        r.Fake.SettingsRequests.Should().Be(0);
    }
}
