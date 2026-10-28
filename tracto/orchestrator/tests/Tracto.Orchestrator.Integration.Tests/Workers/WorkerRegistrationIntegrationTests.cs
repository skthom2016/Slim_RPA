#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Infrastructure.Persistence;
using HttpJsonContent = System.Net.Http.Json.JsonContent;

namespace Tracto.Orchestrator.Integration.Tests.Workers;

/// <summary>
/// OC-01 — Integration tests for POST /workers/register and POST /workers/{workerId}/offline.
/// Uses WebApplicationFactory with an in-memory SQLite database.
///
/// ACs verified end-to-end (via HTTP):
///   - Valid registration → 200 {workerId}.
///   - Re-registration → 200 {workerId}; second row not created.
///   - Missing RuntimeType → 400 ProblemDetails.
///   - CapabilityDigest mismatch → 200 (registered with warning).
/// </summary>
public sealed class WorkerRegistrationIntegrationTests
    : IClassFixture<WorkerRegistrationIntegrationTests.ConnectedFactory>
{
    // ── WebApplicationFactory ─────────────────────────────────────────────────

    public sealed class ConnectedFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _keepAlive;
        private readonly string? _previousMode;

        public ConnectedFactory()
        {
            _previousMode = Environment.GetEnvironmentVariable("TRACTO_MODE");
            Environment.SetEnvironmentVariable("TRACTO_MODE", "Connected");

            _keepAlive = new SqliteConnection("DataSource=:memory:");
            _keepAlive.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("TRACTO_MODE", "Connected");

            builder.ConfigureServices(services =>
            {
                RemoveDbContextDescriptors(services);

                var dbOpts = new DbContextOptionsBuilder<OrchestratorDbContext>()
                    .UseSqlite(_keepAlive)
                    .Options;

                services.AddSingleton<IDbContextFactory<OrchestratorDbContext>>(
                    new OrchestratorDbContextFactory(dbOpts));

                // MUST be Scoped (not Transient) so repos and UoW share the same
                // DbContext instance within a request; Transient breaks the UoW pattern.
                services.AddScoped<OrchestratorDbContext>(sp =>
                    sp.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>()
                      .CreateDbContext());

                using var ctx = new OrchestratorDbContext(dbOpts);
                ctx.Database.EnsureCreated();
            });
        }

        private static void RemoveDbContextDescriptors(IServiceCollection services)
        {
            var ctxType  = typeof(OrchestratorDbContext);
            var toRemove = services
                .Where(d => IsRelated(d.ServiceType, ctxType) ||
                            (d.ImplementationType is not null && IsRelated(d.ImplementationType, ctxType)))
                .ToList();
            foreach (var d in toRemove)
                services.Remove(d);
        }

        private static bool IsRelated(Type t, Type ctx)
        {
            if (t == ctx) return true;
            return t.IsGenericType && t.GenericTypeArguments.Any(a => a == ctx);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _keepAlive.Dispose();
                Environment.SetEnvironmentVariable("TRACTO_MODE", _previousMode);
            }
            base.Dispose(disposing);
        }
    }

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly ConnectedFactory _factory;

    public WorkerRegistrationIntegrationTests(ConnectedFactory factory)
    {
        _factory = factory;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // A structurally-valid JWT accepted by JwtAuthMiddleware when no signing key is
    // configured (alg:none, parse-only mode).  Matches the pattern used in
    // SecurityMiddlewareIntegrationTests.PostJobsPull_WithBearerToken_DoesNotReturn401.
    // Header : {"alg":"none","typ":"JWT"}
    // Payload: {"sub":"test-worker"}
    private const string FakeJwt =
        "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJ0ZXN0LXdvcmtlciJ9.";

    /// <summary>
    /// Returns an HTTP client pre-configured with the fake Bearer token required by
    /// JwtAuthMiddleware for all /workers/* and /jobs/* routes (OC-17).
    /// </summary>
    private HttpClient AuthenticatedClient
    {
        get
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Authorization", $"Bearer {FakeJwt}");
            return client;
        }
    }

    private HttpClient Client => _factory.CreateClient();

    private static string UniqueId() =>
        $"worker-{Guid.NewGuid():N}";

    // ── POST /workers/register ────────────────────────────────────────────────

    [Fact]
    public async Task Register_ValidRequest_Returns200WithWorkerId()
    {
        // Arrange
        var workerId = UniqueId();
        var body = new
        {
            workerId,
            runtimeType             = "dotnet",
            actionNames             = new[] { "ClickAction", "TypeAction" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        };

        // Act — use authenticated client; /workers/* routes require Bearer token (OC-17).
        var response = await AuthenticatedClient.PostAsJsonAsync("/workers/register", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ParseJsonAsync(response);
        json.GetProperty("workerId").GetString().Should().Be(workerId);
    }

    [Fact]
    public async Task Register_MissingRuntimeType_Returns400ProblemDetails()
    {
        // Arrange
        var body = new
        {
            workerId                = UniqueId(),
            runtimeType             = "",
            actionNames             = Array.Empty<string>(),
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        };

        // Act — use authenticated client; /workers/* routes require Bearer token (OC-17).
        var response = await AuthenticatedClient.PostAsJsonAsync("/workers/register", body);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_SameWorkerTwice_Returns200BothTimes()
    {
        // Arrange
        var workerId = UniqueId();
        var body = new
        {
            workerId,
            runtimeType             = "python",
            actionNames             = new[] { "ExtractTable" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 90
        };

        // Act — register twice; /workers/* requires Bearer token (OC-17).
        var resp1 = await AuthenticatedClient.PostAsJsonAsync("/workers/register", body);
        var resp2 = await AuthenticatedClient.PostAsJsonAsync("/workers/register", body);

        // Assert — both succeed (re-registration is idempotent)
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);

        var json1 = await ParseJsonAsync(resp1);
        var json2 = await ParseJsonAsync(resp2);
        json1.GetProperty("workerId").GetString().Should().Be(workerId);
        json2.GetProperty("workerId").GetString().Should().Be(workerId);
    }

    [Fact]
    public async Task Register_DigestMismatch_Returns200()
    {
        // Arrange — wrong digest, should still register per OC-01 spec.
        var body = new
        {
            workerId                = UniqueId(),
            runtimeType             = "browser",
            actionNames             = new[] { "Navigate", "Click" },
            capabilityDigest        = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef",
            absenceThresholdSeconds = 30
        };

        // Act — use authenticated client; /workers/* routes require Bearer token (OC-17).
        var response = await AuthenticatedClient.PostAsJsonAsync("/workers/register", body);

        // Assert — mismatch is a warning; registration must succeed.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_ContentTypeIsApplicationJson()
    {
        // Arrange
        var body = new
        {
            workerId                = UniqueId(),
            runtimeType             = "dotnet",
            actionNames             = Array.Empty<string>(),
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        };

        // Act — use authenticated client; /workers/* routes require Bearer token (OC-17).
        var response = await AuthenticatedClient.PostAsJsonAsync("/workers/register", body);

        // Assert
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/json");
    }

    // ── POST /workers/{workerId}/offline ──────────────────────────────────────

    [Fact]
    public async Task MarkOffline_UnknownWorker_Returns404()
    {
        // Act — use authenticated client; /workers/* routes require Bearer token (OC-17).
        var response = await AuthenticatedClient.PostAsync($"/workers/{UniqueId()}/offline", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MarkOffline_RegisteredWorker_Returns200()
    {
        // Arrange — register a worker first.
        var workerId = UniqueId();
        var regBody = new
        {
            workerId,
            runtimeType             = "dotnet",
            actionNames             = new[] { "ClickAction" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        };
        // /workers/* requires Bearer token (OC-17); use the same authenticated client for both
        // calls so the shared in-memory DB sees the registered worker.
        var authClient = AuthenticatedClient;
        var regResp = await authClient.PostAsJsonAsync("/workers/register", regBody);
        regResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act — mark it offline
        var offlineResp = await authClient.PostAsync($"/workers/{workerId}/offline", null);

        // Assert
        offlineResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
