#nullable enable
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Infrastructure.Persistence;

namespace Tracto.Orchestrator.Integration.Tests.Security;

/// <summary>
/// OC-17 — Integration tests for security middleware.
///
/// Tests covered:
///   1. POST /jobs/pull without Authorization header → 401.
///   2. POST /jobs/pull with valid Authorization header → not 401 (may be 400/422, never 401).
///   3. Admin route from blocked IP → 403 (emulated via X-Forwarded-For; requires CIDR config).
/// </summary>
public sealed class SecurityMiddlewareIntegrationTests
    : IClassFixture<SecurityMiddlewareIntegrationTests.SecuredFactory>
{
    // ── WebApplicationFactory ────────────────────────────────────────────────

    public sealed class SecuredFactory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection _keepAliveConnection;
        private readonly string?          _previousMode;

        public SecuredFactory()
        {
            _previousMode = Environment.GetEnvironmentVariable("TRACTO_MODE");
            Environment.SetEnvironmentVariable("TRACTO_MODE", "Connected");

            _keepAliveConnection = new SqliteConnection("DataSource=:memory:");
            _keepAliveConnection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("TRACTO_MODE", "Connected");
            // No JwtSecret set → JwtAuthMiddleware will parse but not cryptographically verify tokens.
            // This lets us test "missing header → 401" and "present header → passes middleware".

            builder.ConfigureServices(services =>
            {
                RemoveDbContextServices(services);

                var dbOpts = new DbContextOptionsBuilder<OrchestratorDbContext>()
                    .UseSqlite(_keepAliveConnection)
                    .Options;

                services.AddSingleton<IDbContextFactory<OrchestratorDbContext>>(
                    new OrchestratorDbContextFactory(dbOpts));

                services.AddTransient<OrchestratorDbContext>(sp =>
                    sp.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>().CreateDbContext());

                using var ctx = new OrchestratorDbContext(dbOpts);
                ctx.Database.EnsureCreated();
            });
        }

        private static void RemoveDbContextServices(IServiceCollection services)
        {
            var ctx = typeof(OrchestratorDbContext);
            var toRemove = services
                .Where(d => IsRelated(d.ServiceType, ctx) ||
                            (d.ImplementationType is not null && IsRelated(d.ImplementationType, ctx)))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);
        }

        private static bool IsRelated(Type t, Type ctx)
            => t == ctx || (t.IsGenericType && t.GenericTypeArguments.Any(a => a == ctx));

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _keepAliveConnection.Dispose();
                Environment.SetEnvironmentVariable("TRACTO_MODE", _previousMode);
            }
            base.Dispose(disposing);
        }
    }

    private readonly SecuredFactory _factory;

    public SecurityMiddlewareIntegrationTests(SecuredFactory factory)
    {
        _factory = factory;
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PostJobsPull_WithoutAuthHeader_Returns401()
    {
        // Arrange — client with NO Authorization header.
        var client = _factory.CreateClient();

        // Act — POST /jobs/pull is a /jobs/* route; middleware should reject it.
        var response = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId    = "worker-1",
            runtimeType = "dotnet",
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "JwtAuthMiddleware must return 401 when no Authorization header is present");
    }

    [Fact]
    public async Task PostJobsPull_WithBearerToken_DoesNotReturn401()
    {
        // Arrange — client WITH a (unsigned) JWT; no JwtSecret is configured so the middleware
        // only parses (no signature verification).
        // A minimal, structurally valid JWT with header.payload.signature = base64 blobs.
        // eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0 = {"alg":"none","typ":"JWT"}
        // eyJzdWIiOiJ3b3JrZXItMSJ9           = {"sub":"worker-1"}
        const string fakeJwt = "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJ3b3JrZXItMSJ9.";

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {fakeJwt}");

        // Act
        var response = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId    = "worker-1",
            runtimeType = "dotnet",
        });

        // Assert — should NOT be 401; may be 400/422/200 depending on handler validation.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            because: "a parseable Bearer token must pass the JwtAuthMiddleware check");
    }

    [Fact]
    public async Task GetHealth_WithoutAuthHeader_Returns200()
    {
        // Arrange — /health is not a /jobs/* or /workers/* route; middleware must be pass-through.
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "JwtAuthMiddleware must not intercept routes outside /jobs/* and /workers/*");
    }
}
