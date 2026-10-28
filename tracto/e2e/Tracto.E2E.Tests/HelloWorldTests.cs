using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Tracto.E2E.Tests;

/// <summary>
/// E2E-HW-01..04: Hello World external service capability checks.
/// These tests verify the external business service is reachable and
/// responds correctly — a prerequisite for the full orchestrator E2E flow.
/// </summary>
[Collection("E2E")]
public sealed class HelloWorldTests(E2EFixture fixture) : IClassFixture<E2EFixture>
{
    // E2E-HW-01: Health check
    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await fixture.HelloWorldClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal("healthy", body.GetProperty("status").GetString());
    }

    // E2E-HW-02: GET /api/hello with no name returns "Hello, World!"
    [Fact]
    public async Task Hello_NoName_ReturnsHelloWorld()
    {
        var response = await fixture.HelloWorldClient.GetAsync("/api/hello");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal("Hello, World!", body.GetProperty("message").GetString());
    }

    // E2E-HW-03: GET /api/hello?name=Tracto returns personalized greeting
    [Fact]
    public async Task Hello_WithName_ReturnsPersonalizedGreeting()
    {
        var response = await fixture.HelloWorldClient.GetAsync("/api/hello?name=Tracto");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal("Hello, Tracto!", body.GetProperty("message").GetString());
    }

    // E2E-HW-04: POST /api/echo echoes back the request body
    [Fact]
    public async Task Echo_ReturnsBodyVerbatim()
    {
        const string payload = "ping-from-tracto-e2e";
        var content = new StringContent(payload, Encoding.UTF8, "text/plain");
        var response = await fixture.HelloWorldClient.PostAsync("/api/echo", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        Assert.Equal(payload, body.GetProperty("echo").GetString());
        Assert.Equal(payload.Length, body.GetProperty("length").GetInt32());
    }
}
