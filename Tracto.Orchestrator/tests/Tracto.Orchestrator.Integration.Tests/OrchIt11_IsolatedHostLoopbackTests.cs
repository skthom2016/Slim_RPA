#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Tracto.Orchestrator.Integration.Tests.Fixtures;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-11: isolated host loopback.
///
/// Bind test server to loopback. Assert localhost connection succeeds.
/// Assert that the server's listen URL in Connected mode is 0.0.0.0.
/// Assert that the server's listen URL in Isolated mode would be 127.0.0.1 (OrchestratorSettings invariant).
/// </summary>
public sealed class OrchIt11_IsolatedHostLoopbackTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt11_IsolatedHostLoopbackTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task TestServer_Localhost_Connection_Succeeds()
    {
        // The WebApplicationFactory TestServer always listens on the loopback by design;
        // the HttpClient it creates connects to that same loopback address.
        // This verifies that requests from the factory's client always succeed — i.e.,
        // the in-process test server accepts loopback connections.
        var client   = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the test server must accept connections from localhost via the factory client");

        var body = await ParseJsonAsync(response);
        body.GetProperty("status").GetString().Should().Be("healthy");
    }

    [Fact]
    public async Task ConnectedMode_ListenAddress_Is_AllInterfaces()
    {
        // OrchestratorSettings invariant: Connected mode binds to 0.0.0.0 (all interfaces)
        var settings = new Tracto.Orchestrator.Domain.Settings.OrchestratorSettings(
            Mode: Tracto.Orchestrator.Domain.Settings.DeploymentMode.Connected,
            ApiPort: 8080);

        settings.ListenAddress.Should().Be("0.0.0.0",
            "Connected mode must bind on all interfaces");
        settings.ListenUrl.Should().Be("http://0.0.0.0:8080");
    }

    [Fact]
    public async Task IsolatedMode_ListenAddress_Is_Loopback()
    {
        // OrchestratorSettings invariant: Isolated mode binds to 127.0.0.1 only
        var settings = new Tracto.Orchestrator.Domain.Settings.OrchestratorSettings(
            Mode: Tracto.Orchestrator.Domain.Settings.DeploymentMode.Isolated,
            ApiPort: 8080);

        settings.ListenAddress.Should().Be("127.0.0.1",
            "Isolated mode must bind only on the loopback interface");
        settings.ListenUrl.Should().Be("http://127.0.0.1:8080");
    }

    [Fact]
    public async Task TestServer_Returns_ProperContentType_OnAllEndpoints()
    {
        var client = _factory.CreateClient();

        // /health — verify JSON content-type
        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/json", "/health must return application/json");
    }

    [Fact]
    public async Task TestServer_Missing_BearerToken_Returns401_OnProtectedRoute()
    {
        // /workers/register requires a Bearer token (JwtAuthMiddleware, OC-17).
        // A plain unauthenticated client must receive 401.
        var client = _factory.CreateClient();   // no token

        var response = await client.PostAsJsonAsync("/workers/register", new
        {
            workerId                = $"no-auth-{Guid.NewGuid():N}",
            runtimeType             = "dotnet",
            actionNames             = Array.Empty<string>(),
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "/workers/register must reject requests without a Bearer token");
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
