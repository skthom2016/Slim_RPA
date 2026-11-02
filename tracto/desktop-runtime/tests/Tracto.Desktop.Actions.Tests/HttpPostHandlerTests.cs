using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tracto.Desktop.Actions;
using Tracto.Desktop.Actions.Handlers;
using Xunit;

namespace Tracto.Desktop.Actions.Tests;

public class HttpPostHandlerTests
{
    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? SentBody;
        public string? SentContentType;
        public Uri? SentUri;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            SentUri = request.RequestUri;
            SentBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            SentContentType = request.Content?.Headers.ContentType?.MediaType;
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static ActionContext Context(FakeHandler fake, Dictionary<string, object?> inputs) => new()
    {
        StepId = "step.1",
        Inputs = inputs,
        Vars = new Dictionary<string, object?>(),
        HttpClient = new HttpClient(fake),
        Logger = NullLogger.Instance,
        CancellationToken = CancellationToken.None,
    };

    [Fact]
    public async Task Posts_the_body_as_json_and_exposes_top_level_fields_as_outputs()
    {
        var fake = new FakeHandler(HttpStatusCode.OK, """{"updated":2,"text":"Row\tA\r\n32\tx","nested":{"a":1}}""");
        var handler = new HttpPostHandler(NullLogger<HttpPostHandler>.Instance);

        var result = await handler.ExecuteAsync(Context(fake, new()
        {
            ["url"] = "http://localhost:5090/apply",
            ["body"] = """{"limit":2}""",
        }));

        result.Success.Should().BeTrue();
        fake.SentBody.Should().Be("""{"limit":2}""");
        fake.SentContentType.Should().Be("application/json");
        result.Outputs!["statusCode"].Should().Be(200);
        result.Outputs["updated"].Should().Be("2");
        result.Outputs["text"].Should().Be("Row\tA\r\n32\tx");
        result.Outputs.Should().NotContainKey("nested"); // only scalar fields are flattened
    }

    [Fact]
    public async Task A_non_success_status_fails_the_step_and_keeps_the_body()
    {
        var fake = new FakeHandler(HttpStatusCode.BadGateway, """{"error":"file is open"}""");
        var handler = new HttpPostHandler(NullLogger<HttpPostHandler>.Instance);

        var result = await handler.ExecuteAsync(Context(fake, new() { ["url"] = "http://x/apply", ["body"] = "{}" }));

        result.Success.Should().BeFalse();
        result.Outputs!["error"].Should().Be("file is open");
    }

    [Fact]
    public async Task Url_is_required()
    {
        var handler = new HttpPostHandler(NullLogger<HttpPostHandler>.Instance);
        var result = await handler.ExecuteAsync(Context(new FakeHandler(HttpStatusCode.OK, "{}"), new()));
        result.Success.Should().BeFalse();
    }
}
