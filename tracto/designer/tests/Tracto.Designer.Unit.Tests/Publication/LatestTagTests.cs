namespace Tracto.Designer.Unit.Tests.Publication;

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Publication;
using Tracto.Designer.Core.Scripting;
using Tracto.Designer.Core.Testing;
using Tracto.Designer.Core.Validation;
using Tracto.Designer.Infrastructure.Publication;

/// <summary>
/// "Set as latest" moves the Orchestrator's "latest" tag to the version just published. It starts
/// off, so publishing a version for testing never changes what is live.
/// </summary>
public class LatestTagTests
{
    private sealed record Seen(HttpMethod Method, string Path, string Body);

    /// <summary>Answers every request with the status chosen for its path and records what was asked.</summary>
    private sealed class Orchestrator : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];
        public HttpStatusCode VersionStatus { get; set; } = HttpStatusCode.Created;
        public HttpStatusCode TagStatus { get; set; } = HttpStatusCode.OK;
        /// <summary>The body the Orchestrator answers a version publish with.</summary>
        public string VersionReply { get; set; } = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Seen(request.Method, request.RequestUri!.AbsolutePath, body));
            var isTag = request.RequestUri.AbsolutePath.Contains("/aliases/");
            var status = isTag ? TagStatus : VersionStatus;
            var reply = isTag ? "{}" : VersionReply;
            return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }

    private static Script ValidScript(string version = "1.0.3")
    {
        var s = new Script { Id = "child", Name = "Child", Version = version, TargetRuntime = "desktop" };
        s.Steps.Add(new ScriptStep { StepNumber = 1, Description = "Click", Action = "click", Parameters = { ["coordinates"] = "10,10" } });
        return s;
    }

    private static (OrchestratorPublisher Publisher, Orchestrator Server) Publisher()
    {
        var server = new Orchestrator();
        var http = new HttpClient(server) { BaseAddress = new Uri("http://orchestrator.test/") };
        var gate = new PublicationGate(new ScriptValidator(), testRequired: false);
        return (new OrchestratorPublisher(http, gate), server);
    }

    private static PublicationRequest Request(bool setAsLatest, Script? script = null) =>
        new(script ?? ValidScript(), ScriptTestResult.NotRun, "orchestrator", setAsLatest);

    [Fact]
    public void Publishing_Does_Not_Move_The_Tag_By_Default()
    {
        new PublicationRequest(ValidScript(), ScriptTestResult.NotRun, "x").SetAsLatest.Should().BeFalse();
    }

    [Fact]
    public async Task Without_The_Option_Only_The_Version_Is_Published()
    {
        var (publisher, server) = Publisher();

        var result = await publisher.PublishAsync(Request(setAsLatest: false));

        result.Success.Should().BeTrue();
        result.MovedLatest.Should().BeFalse();
        server.Requests.Should().ContainSingle()
            .Which.Should().Match<Seen>(r => r.Method == HttpMethod.Put && r.Path == "/artifacts/child/versions/1.0.3");
    }

    [Fact]
    public async Task With_The_Option_The_Tag_Is_Moved_After_The_Version_Is_Stored()
    {
        var (publisher, server) = Publisher();

        var result = await publisher.PublishAsync(Request(setAsLatest: true));

        result.Success.Should().BeTrue();
        result.MovedLatest.Should().BeTrue();
        server.Requests.Select(r => $"{r.Method} {r.Path}").Should().Equal(
            "PUT /artifacts/child/versions/1.0.3",
            "PUT /artifacts/child/aliases/latest");

        using var body = JsonDocument.Parse(server.Requests[1].Body);
        body.RootElement.GetProperty("version").GetString().Should().Be("1.0.3");
    }

    [Fact]
    public async Task A_Refused_Version_Never_Moves_The_Tag()
    {
        var (publisher, server) = Publisher();
        server.VersionStatus = HttpStatusCode.Conflict; // the version exists with different content

        var result = await publisher.PublishAsync(Request(setAsLatest: true));

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("409");
        server.Requests.Should().ContainSingle("the tag must not move for a version that was not stored");
    }

    [Fact]
    public async Task A_Failed_Tag_Move_Says_The_Version_Was_Published_But_Latest_Did_Not_Move()
    {
        var (publisher, server) = Publisher();
        server.TagStatus = HttpStatusCode.NotFound;

        var result = await publisher.PublishAsync(Request(setAsLatest: true));

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("1.0.3 was published").And.Contain("'latest' could not be moved");
    }

    [Fact]
    public async Task An_Invalid_Script_Sends_Nothing_Even_With_The_Option()
    {
        var (publisher, server) = Publisher();
        var invalid = new Script { Id = "", Name = "", Version = "", TargetRuntime = "" };

        var result = await publisher.PublishAsync(Request(setAsLatest: true, invalid));

        result.Success.Should().BeFalse();
        server.Requests.Should().BeEmpty();
    }

    // ── publishing a version the Orchestrator already holds ──────────────────

    [Theory]
    [InlineData("{\"artifactId\":\"child\",\"version\":\"1.0.3\",\"alreadyPublished\":true}", true)]
    [InlineData("{\"artifactId\":\"child\",\"version\":\"1.0.3\",\"alreadyPublished\":false}", false)]
    [InlineData("{\"artifactId\":\"child\",\"version\":\"1.0.3\"}", false)]   // an Orchestrator that does not send the field
    [InlineData("not json at all", false)]
    [InlineData("", false)]
    public async Task Reports_When_The_Version_Was_Already_There(string reply, bool expected)
    {
        var (publisher, server) = Publisher();
        server.VersionReply = reply;

        var result = await publisher.PublishAsync(Request(setAsLatest: false));

        result.Success.Should().BeTrue();
        result.AlreadyPublished.Should().Be(expected);
        if (expected) result.Message.Should().Contain("nothing was added");
    }

    [Fact]
    public async Task A_Repeat_With_Set_As_Latest_Still_Moves_The_Tag_And_Says_Nothing_Was_Added()
    {
        var (publisher, server) = Publisher();
        server.VersionReply = "{\"alreadyPublished\":true}";

        var result = await publisher.PublishAsync(Request(setAsLatest: true));

        result.Success.Should().BeTrue();
        result.AlreadyPublished.Should().BeTrue();
        result.MovedLatest.Should().BeTrue();
        server.Requests.Should().HaveCount(2, "promoting an unchanged version is a valid reason to publish it again");
    }

    [Fact]
    public async Task The_Output_Says_Nothing_New_Was_Published_Instead_Of_Published()
    {
        var editor = new ScriptEditorViewModel();
        editor.LoadScript(ValidScript());
        var testing = new TestPanelViewModel(new LocalSimulationTestRunner(new ScriptValidator()), editor);
        var publisher = new Mock<IScriptPublisher>();
        publisher.SetupGet(p => p.Name).Returns("test publisher");
        publisher.Setup(p => p.PublishAsync(It.IsAny<PublicationRequest>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(PublicationResult.Ok("child", alreadyPublished: true));
        var log = new OutputLogViewModel();
        var vm = new PublicationViewModel(publisher.Object, editor, testing, testRequired: false, output: log);

        vm.PublishCommand.Execute(null);
        await WaitUntilIdle(vm);

        log.Entries.Should().Contain(e => e.Severity == OutputSeverity.Warning
            && e.Message.Contains("Nothing new was published")
            && e.Message.Contains("1.0.3")
            && e.Message.Contains("raise the version"));
        log.Entries.Should().NotContain(e => e.Message.StartsWith("Published"),
            "a repeat must not read like a fresh publish");
    }

    // ── the checkbox ─────────────────────────────────────────────────────────

    private static async Task WaitUntilIdle(PublicationViewModel vm)
    {
        for (var i = 0; i < 200 && (vm.IsPublishing || !vm.HasResult); i++)
            await Task.Delay(25);
    }

    private static (PublicationViewModel Vm, List<PublicationRequest> Seen, Mock<IScriptPublisher> Publisher) ViewModel(PublicationResult result)
    {
        var editor = new ScriptEditorViewModel();
        editor.LoadScript(ValidScript());
        var testing = new TestPanelViewModel(new LocalSimulationTestRunner(new ScriptValidator()), editor);

        var seen = new List<PublicationRequest>();
        var publisher = new Mock<IScriptPublisher>();
        publisher.SetupGet(p => p.Name).Returns("test publisher");
        publisher.Setup(p => p.PublishAsync(It.IsAny<PublicationRequest>(), It.IsAny<CancellationToken>()))
                 .Callback<PublicationRequest, CancellationToken>((r, _) => seen.Add(r))
                 .ReturnsAsync(result);
        return (new PublicationViewModel(publisher.Object, editor, testing, testRequired: false), seen, publisher);
    }

    [Fact]
    public async Task The_Checkbox_Starts_Off_And_A_Plain_Publish_Does_Not_Ask_For_Latest()
    {
        var (vm, seen, _) = ViewModel(PublicationResult.Ok("PUB-1"));
        vm.SetAsLatest.Should().BeFalse();

        vm.PublishCommand.Execute(null);
        await WaitUntilIdle(vm);

        seen.Should().ContainSingle().Which.SetAsLatest.Should().BeFalse();
    }

    [Fact]
    public async Task Ticking_The_Checkbox_Asks_For_Latest_Then_Switches_Itself_Off()
    {
        var (vm, seen, _) = ViewModel(PublicationResult.Ok("PUB-1", movedLatest: true));
        vm.SetAsLatest = true;

        vm.PublishCommand.Execute(null);
        await WaitUntilIdle(vm);

        seen.Should().ContainSingle().Which.SetAsLatest.Should().BeTrue();
        vm.SetAsLatest.Should().BeFalse("promoting a version is chosen each time, not left on");
    }

    [Fact]
    public async Task The_Checkbox_Stays_On_After_A_Failed_Publish_So_It_Can_Be_Retried()
    {
        var (vm, _, _) = ViewModel(PublicationResult.Failure("Orchestrator returned 503"));
        vm.SetAsLatest = true;

        vm.PublishCommand.Execute(null);
        await WaitUntilIdle(vm);

        vm.SetAsLatest.Should().BeTrue();
    }
}
