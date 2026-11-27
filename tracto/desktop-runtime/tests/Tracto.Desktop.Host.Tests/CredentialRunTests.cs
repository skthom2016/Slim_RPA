#nullable enable
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracto.Contract.Models;
using Tracto.Contract.Models.Execution;
using Tracto.Desktop.Actions;
using Tracto.Desktop.Host;
using Tracto.Desktop.Protocol;
using Xunit;

namespace Tracto.Desktop.Host.Tests;

/// <summary>
/// <c>${env:NAME}</c> references in desktop steps: what the engine asks the Orchestrator for,
/// what the handlers receive, and that no secret value leaves the run in a log, an error or an
/// output. The handlers here only record; nothing touches the desktop.
/// </summary>
public sealed class CredentialRunTests : IDisposable
{
    private const string User = "fin.ops@acme.com";
    private const string Pass = "Zebra#Secret-4471";

    public void Dispose() => SecretMask.Clear();

    /// <summary>An action handler that records its inputs and answers as told.</summary>
    private sealed class Recording(string action, Func<ActionContext, ActionResult>? respond = null) : IActionHandler
    {
        public string ActionName => action;
        public List<IReadOnlyDictionary<string, object?>> Inputs { get; } = [];

        public Task<ActionResult> ExecuteAsync(ActionContext ctx)
        {
            Inputs.Add(new Dictionary<string, object?>(ctx.Inputs));
            return Task.FromResult(respond?.Invoke(ctx) ?? ActionResult.Ok());
        }
    }

    private sealed class FakeSource : IRunSecretSource
    {
        public List<string[]> EnvRequests { get; } = [];
        public string? Refusal { get; set; }
        public string Password { get; set; } = Pass;

        private EnvValue Resolve(string name) =>
            name.EndsWith("_PASSWORD", StringComparison.Ordinal) ? new EnvValue(Password, true)
            : name.EndsWith("_CODE", StringComparison.Ordinal) ? new EnvValue("123456", true)
            : name.EndsWith("_USERNAME", StringComparison.Ordinal) ? new EnvValue(User, false)
            : new EnvValue("https://portal.example.com", false);

        public Task<IReadOnlyDictionary<string, EnvValue>> GetEnvAsync(
            string runId, IReadOnlyCollection<string> names, CancellationToken ct)
        {
            EnvRequests.Add(names.ToArray());
            if (Refusal is not null) throw new CredentialRefusedException(Refusal);
            IReadOnlyDictionary<string, EnvValue> r = names.ToDictionary(n => n, Resolve);
            return Task.FromResult(r);
        }
    }

    private sealed class ArtifactHandler(string envelope) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonSerializer.Serialize(new
            {
                payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(envelope)),
                contentHash = "unused"
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static string Envelope(params string[] steps) =>
        "{\"script\":{\"Id\":\"t\",\"Name\":\"t\",\"Version\":\"1.0.0\",\"steps\":[" +
        string.Join(",", steps.Select((s, i) => "{\"StepNumber\":" + (i + 1) + "," + s + "}")) + "]}}";

    private sealed record Ran(RunState State, List<StepResult> Steps, Dictionary<string, string> Outputs,
        IReadOnlyList<RunLogEntry> Log);

    private static async Task<Ran> RunAsync(string envelope, FakeSource source,
        IEnumerable<IActionHandler> handlers, Dictionary<string, string>? runParams = null)
    {
        var sink = new RunLogSink();
        using var loggers = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Information).AddProvider(new RunLogForwarder(sink)));
        var engine = new ScriptExecutionEngine(
            new ActionDispatcher(handlers, NullLogger<ActionDispatcher>.Instance),
            new HttpClient(new ArtifactHandler(envelope)) { BaseAddress = new Uri("http://orchestrator.test/") },
            loggers.CreateLogger<ScriptExecutionEngine>(),
            source);

        var job = new JobContext
        {
            LeaseId = "lease-1", AttemptId = "att-1", RunId = "run-1", ArtifactUrl = "artifacts/t/versions/1.0.0",
            ArtifactDigest = "", LeaseDurationMs = 60_000
        };
        var outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        sink.BeginRun(job.RunId, job.AttemptId);
        var (state, steps) = await engine.RunAsync(job, runParams ?? new Dictionary<string, string>(), outputs, CancellationToken.None);
        return new Ran(state, steps, outputs, sink.EndRunAndDrain());
    }

    private static string Everything(Ran r) =>
        string.Join("\n", r.Steps.Select(s => s.ErrorDetail)) + "\n" +
        string.Join("\n", r.Outputs.Select(o => o.Key + "=" + o.Value)) + "\n" +
        string.Join("\n", r.Log.Select(l => l.Message));

    [Fact]
    public async Task A_step_receives_the_real_values_and_nothing_leaks()
    {
        var type = new Recording("type");
        var source = new FakeSource();
        var r = await RunAsync(Envelope(
            "\"Action\":\"type\",\"Parameters\":{\"value\":\"${env:SITE_A_USERNAME}\"}",
            "\"Action\":\"type\",\"Parameters\":{\"value\":\"pw=${env:SITE_A_PASSWORD}!\"}",
            "\"Action\":\"type\",\"Parameters\":{\"value\":\"${env:SITE_A_CODE}\"}",
            "\"Action\":\"type\",\"Parameters\":{\"value\":\"${env:PORTAL_URL}/login\"}"), source, [type]);

        r.State.Should().Be(RunState.Succeeded);
        type.Inputs.Select(i => i["value"]).Should().Equal(User, "pw=" + Pass + "!", "123456", "https://portal.example.com/login");
        source.EnvRequests.Should().ContainSingle().Which.Should()
            .Equal("PORTAL_URL", "SITE_A_CODE", "SITE_A_PASSWORD", "SITE_A_USERNAME");
        Everything(r).Should().NotContain(Pass).And.NotContain("123456");
    }

    [Fact]
    public async Task A_variable_name_can_be_built_from_a_job_parameter()
    {
        var type = new Recording("type");
        var source = new FakeSource();
        var r = await RunAsync(Envelope(
            "\"Action\":\"type\",\"Parameters\":{\"value\":\"${env:${param.site}_USERNAME}\"}"),
            source, [type], new Dictionary<string, string> { ["site"] = "SITE1" });

        r.State.Should().Be(RunState.Succeeded);
        source.EnvRequests.Should().ContainSingle().Which.Should().Equal("SITE1_USERNAME");
        type.Inputs.Single()["value"].Should().Be(User);
    }

    [Fact]
    public async Task A_password_that_looks_like_a_variable_is_typed_as_it_is()
    {
        const string tricky = "P${now.date}x${param.site}!";
        var type = new Recording("type");
        var r = await RunAsync(Envelope(
            "\"Action\":\"type\",\"Parameters\":{\"value\":\"${env:SITE_A_PASSWORD}\"}"),
            new FakeSource { Password = tricky }, [type], new Dictionary<string, string> { ["site"] = "zzz" });

        r.State.Should().Be(RunState.Succeeded);
        type.Inputs.Single()["value"].Should().Be(tricky);
    }

    [Fact]
    public async Task A_step_that_would_store_a_secret_as_an_output_fails()
    {
        var source = new FakeSource();
        var echo = new Recording("clipboard.get", ctx => ActionResult.Ok(new Dictionary<string, object?> { ["text"] = ctx.GetString("echo") }));
        var r = await RunAsync(Envelope(
            "\"Action\":\"clipboard.get\",\"Parameters\":{\"echo\":\"${env:SITE_A_PASSWORD}\",\"storeAs\":\"leak\"}"),
            source, [echo]);

        r.State.Should().Be(RunState.Failed);
        r.Steps.Single().ErrorDetail.Should().Contain("CREDENTIAL_IN_OUTPUT");
        r.Outputs.Should().NotContainKey("leak.text");
        Everything(r).Should().NotContain(Pass);
    }

    [Fact]
    public async Task A_plain_value_may_be_kept_as_an_output()
    {
        var echo = new Recording("clipboard.get", ctx => ActionResult.Ok(new Dictionary<string, object?> { ["text"] = ctx.GetString("echo") }));
        var r = await RunAsync(Envelope(
            "\"Action\":\"clipboard.get\",\"Parameters\":{\"echo\":\"${env:SITE_A_USERNAME}\",\"storeAs\":\"who\"}"),
            new FakeSource(), [echo]);

        r.State.Should().Be(RunState.Succeeded);
        r.Outputs["who.text"].Should().Be(User);
    }

    [Fact]
    public async Task A_step_without_storeAs_may_pass_a_password_to_its_handler()
    {
        var echo = new Recording("clipboard.get", ctx => ActionResult.Ok(new Dictionary<string, object?> { ["text"] = ctx.GetString("echo") }));
        var r = await RunAsync(Envelope(
            "\"Action\":\"clipboard.get\",\"Parameters\":{\"echo\":\"${env:SITE_A_PASSWORD}\"}"),
            new FakeSource(), [echo]);

        r.State.Should().Be(RunState.Succeeded);
        r.Outputs.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refused_variable_stops_the_run_before_the_first_step()
    {
        var type = new Recording("type");
        var source = new FakeSource { Refusal = "Variable SITE_A_PASSWORD: the script may not read the variables of group finance" };
        var r = await RunAsync(Envelope(
            "\"Action\":\"type\",\"Parameters\":{\"value\":\"${env:SITE_A_PASSWORD}\"}"), source, [type]);

        r.State.Should().Be(RunState.Failed);
        type.Inputs.Should().BeEmpty("no step ran");
        var step = r.Steps.Should().ContainSingle().Subject;
        step.StepId.Should().Be("credentials");
        step.ErrorCategory.Should().Be(ErrorCategory.PolicyDenied);
        step.ErrorDetail.Should().Contain("SITE_A_PASSWORD").And.Contain("finance");
    }

    [Fact]
    public async Task A_name_built_from_an_earlier_steps_output_is_refused_up_front()
    {
        var type = new Recording("type");
        var source = new FakeSource();
        var r = await RunAsync(Envelope(
            "\"Action\":\"type\",\"Parameters\":{\"value\":\"${env:${first.value}_PASSWORD}\"}"), source, [type]);

        r.State.Should().Be(RunState.Failed);
        source.EnvRequests.Should().BeEmpty();
        r.Steps.Single().ErrorDetail.Should().Contain("job parameters");
    }

    [Fact]
    public async Task A_failing_step_that_quotes_the_password_is_masked_in_the_result_and_the_log()
    {
        var failing = new Recording("type", ctx => ActionResult.Fail("EXECUTION_FAILED", "window rejected '" + ctx.GetString("value") + "'"));
        var r = await RunAsync(Envelope(
            "\"Action\":\"type\",\"Parameters\":{\"value\":\"${env:SITE_A_PASSWORD}\"}"), new FakeSource(), [failing]);

        r.State.Should().Be(RunState.Failed);
        r.Steps.Single().ErrorDetail.Should().Contain("●●●●");
        Everything(r).Should().NotContain(Pass);
    }

    [Fact]
    public async Task A_script_without_references_never_asks_the_orchestrator()
    {
        var type = new Recording("type");
        var source = new FakeSource();
        var r = await RunAsync(Envelope(
            "\"Action\":\"type\",\"Parameters\":{\"value\":\"${param.x} ${now.date}\"}"), source, [type],
            new Dictionary<string, string> { ["x"] = "hello" });

        r.State.Should().Be(RunState.Succeeded);
        source.EnvRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_reference_with_no_way_to_read_variables_fails_clearly()
    {
        var sink = new RunLogSink();
        var engine = new ScriptExecutionEngine(
            new ActionDispatcher([new Recording("type")], NullLogger<ActionDispatcher>.Instance),
            new HttpClient(new ArtifactHandler(Envelope(
                "\"Action\":\"type\",\"Parameters\":{\"value\":\"${env:SITE_A_PASSWORD}\"}"))) { BaseAddress = new Uri("http://o.test/") },
            NullLogger<ScriptExecutionEngine>.Instance);
        var job = new JobContext { LeaseId = "l", AttemptId = "a", RunId = "r", ArtifactUrl = "x", ArtifactDigest = "", LeaseDurationMs = 1 };

        var (state, steps) = await engine.RunAsync(job, new Dictionary<string, string>(), new Dictionary<string, string>(), CancellationToken.None);

        state.Should().Be(RunState.Failed);
        steps.Single().ErrorDetail.Should().Contain("cannot read variables");
        _ = sink;
    }
}
