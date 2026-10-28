using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Tracto.E2E.Tests;

/// <summary>
/// Reads base URLs from env vars set by run-e2e.ps1 (or defaults for local dev).
/// Tests use this fixture via IClassFixture&lt;E2EFixture&gt;.
/// </summary>
public sealed class E2EFixture : IDisposable
{
    public string OrchestratorUrl { get; } =
        Environment.GetEnvironmentVariable("TRACTO_ORCHESTRATOR_URL") ?? "http://localhost:8080";

    public string HelloWorldUrl { get; } =
        Environment.GetEnvironmentVariable("TRACTO_HELLO_WORLD_URL") ?? "http://localhost:5200";

    public HttpClient OrchestratorClient { get; }
    public HttpClient HelloWorldClient { get; }

    public E2EFixture()
    {
        OrchestratorClient = new HttpClient { BaseAddress = new Uri(OrchestratorUrl), Timeout = TimeSpan.FromSeconds(30) };
        HelloWorldClient   = new HttpClient { BaseAddress = new Uri(HelloWorldUrl),   Timeout = TimeSpan.FromSeconds(30) };

        // The orchestrator requires a JWT Bearer token on /jobs/* and /workers/*.
        // When no JwtSecret is configured (development/test mode), signature validation is skipped
        // so any well-formed JWT works. We attach a minimal unsigned-payload token.
        OrchestratorClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BuildDummyJwt());
    }

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string BuildDummyJwt()
    {
        // HS256 header (signature will be ignored by the orchestrator in dev mode)
        var header  = Base64UrlEncode(@"{""alg"":""HS256"",""typ"":""JWT""}");
        var payload = Base64UrlEncode(@"{""sub"":""e2e-test"",""name"":""E2E"",""iat"":1000000}");
        var sig     = Base64UrlEncode("e2e-dummy-sig");
        return $"{header}.{payload}.{sig}";
    }

    private static string Base64UrlEncode(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public void Dispose()
    {
        OrchestratorClient.Dispose();
        HelloWorldClient.Dispose();
    }
}
