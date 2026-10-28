using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tracto.E2E.Tests;

/// <summary>
/// E2E-OC-01: Orchestrator health and readiness checks.
/// </summary>
[Collection("E2E")]
public sealed class OrchestratorHealthTests(E2EFixture fixture) : IClassFixture<E2EFixture>
{
    [Fact]
    public async Task Health_ReturnsOkWithStatus()
    {
        var response = await fixture.OrchestratorClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(E2EFixture.Json);
        var status = body.GetProperty("status").GetString();
        Assert.True(status == "Healthy" || status == "healthy" || status == "OK",
            $"Unexpected health status: {status}");
    }
}
