#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Domain.Settings;
using Tracto.Orchestrator.Infrastructure.Persistence;
using Tracto.Orchestrator.Integration.Tests.Fixtures;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-07: serviceOutcome routing and single Run B.
///
/// HANDED_OFF run. Create ContinuationMapping with HANDED_OFF trigger.
/// Complete run with HANDED_OFF result → continuation mapping fires.
/// Repeat with same run (dedup) → still exactly one Run B.
/// </summary>
public sealed class OrchIt07_ServiceOutcomeRoutingTests
    : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public OrchIt07_ServiceOutcomeRoutingTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HandedOff_Triggers_ContinuationRunB_ExactlyOnce()
    {
        var client     = _factory.AuthenticatedClient();
        var workerId   = $"worker-{Guid.NewGuid():N}";
        var artifactId = $"script-orch-it07-{Guid.NewGuid():N}";
        var continuationArtifactId = $"cont-orch-it07-{Guid.NewGuid():N}";

        // Setup artifacts
        await client.PostAsJsonAsync("/workers/register", new
        {
            workerId,
            runtimeType             = "dotnet",
            actionNames             = new[] { "Run" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        });

        await client.PutAsJsonAsync($"/artifacts/{artifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = Convert.ToBase64String("script-a"u8.ToArray()),
            signature       = (string?)null,
            selectorContent = (string?)null
        });

        await client.PutAsJsonAsync($"/artifacts/{continuationArtifactId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = Convert.ToBase64String("script-b"u8.ToArray()),
            signature       = (string?)null,
            selectorContent = (string?)null
        });

        // Create Run A (the source run)
        var jobResp = await client.PostAsJsonAsync("/jobs", new
        {
            scriptArtifactId = artifactId,
            scriptVersion    = "v1",
            schemaVersion    = "1",
            inputRefs        = Array.Empty<string>(),
            secretRefs       = Array.Empty<string>(),
            triggerType      = "Manual"
        });
        jobResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var runAId = Guid.Parse((await ParseJsonAsync(jobResp)).GetProperty("runId").GetString()!);

        // Create a ContinuationMapping: HANDED_OFF → run continuation script
        var mappingResp = await client.PostAsJsonAsync("/continuation-mappings", new
        {
            triggerRunId          = runAId,
            triggerState          = "HandedOff",
            targetArtifactId      = continuationArtifactId,
            targetArtifactVersion = "v1"
        });
        mappingResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var mappingBody = await ParseJsonAsync(mappingResp);
        var mappingId   = Guid.Parse(mappingBody.GetProperty("mappingId").GetString()!);

        // Pull and accept the job (Run A enters RUNNING)
        var pullResp = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId,
            runtimeType     = "dotnet",
            capabilityNames = Array.Empty<string>()
        });
        pullResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pullBody  = await ParseJsonAsync(pullResp);
        pullBody.GetProperty("result").GetString().Should().Be("JobOffered");
        var attemptId = Guid.Parse(pullBody.GetProperty("attemptId").GetString()!);

        await client.PostAsJsonAsync($"/jobs/{runAId}/lease/accept", new { attemptId, workerId });

        // Worker submits HANDED_OFF result — should trigger Run B creation
        var resultResp = await client.PostAsJsonAsync($"/jobs/{runAId}/result", new
        {
            attemptId,
            terminalState = "HandedOff"
        });
        resultResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ParseJsonAsync(resultResp)).GetProperty("terminalState").GetString().Should().Be("HANDEDOFF");

        // Verify that Run B was created in the ContinuationRunRecords
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();

        var continuationRecords = await db.ContinuationRunRecords
            .Where(r => r.SourceRunId == runAId)
            .ToListAsync();
        continuationRecords.Should().HaveCount(1, "HANDED_OFF result must create exactly one Run B");

        var runBId = continuationRecords[0].RunBId;
        runBId.Should().NotBe(Guid.Empty);

        // Verify Run B is QUEUED
        var runB = await db.RunRecords.FirstOrDefaultAsync(r => r.Id == runBId);
        runB.Should().NotBeNull("Run B must exist in RunRecords");
        runB!.State.Should().Be(Tracto.Orchestrator.Domain.Runs.RunState.Queued,
            "continuation Run B must be created in QUEUED state");

        // Dedup: a second identical commit must not create another Run B
        // (the terminal state is already set; the handler returns idempotent)
        var result2Resp = await client.PostAsJsonAsync($"/jobs/{runAId}/result", new
        {
            attemptId,
            terminalState = "HandedOff"
        });
        result2Resp.StatusCode.Should().Be(HttpStatusCode.OK, "duplicate result is idempotent");

        var continuationRecords2 = await db.ContinuationRunRecords
            .Where(r => r.SourceRunId == runAId)
            .ToListAsync();
        continuationRecords2.Should().HaveCount(1,
            "dedup: exactly one Run B even after a duplicate HANDED_OFF result");
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}

/// <summary>
/// ORCH-IT-07 (callback path): HMAC-signed POST /callbacks → Run B QUEUED,
/// then replay the same EventId → 200 DUPLICATE with Run B count still 1.
///
/// This test exercises the actual /callbacks HTTP endpoint with a properly
/// HMAC-signed payload, satisfying the spec AC requirements:
///   - Valid HMAC → 200 Accepted + Run B QUEUED
///   - Replay same EventId → 200 DUPLICATE + Run B count unchanged at 1
///
/// Both assertions are in a single test to avoid state leakage between tests
/// that share the same isolated in-memory database (IClassFixture).
/// </summary>
public sealed class OrchIt07_CallbackHmacTests
    : IClassFixture<OrchIt07_CallbackHmacTests.HmacCallbackTestFactory>
{
    // ── Custom factory: injects a known HMAC key into OrchestratorSettings ───────

    public sealed class HmacCallbackTestFactory : WebApplicationFactory<Program>, IDisposable
    {
        internal const string TestKeyId = "it07-test-key";

        // Fixed 32-byte test key (not secret — test-only).
        internal static readonly byte[] TestKeyBytes =
            [0x00,0x01,0x02,0x03,0x04,0x05,0x06,0x07,
             0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x0E,0x0F,
             0x10,0x11,0x12,0x13,0x14,0x15,0x16,0x17,
             0x18,0x19,0x1A,0x1B,0x1C,0x1D,0x1E,0x1F];

        private readonly SqliteConnection _keepAlive;
        private readonly string?          _previousMode;

        public HmacCallbackTestFactory()
        {
            _previousMode = Environment.GetEnvironmentVariable("TRACTO_MODE");
            Environment.SetEnvironmentVariable("TRACTO_MODE", "Connected");

            var dbName = $"tracto-cb-{Guid.NewGuid():N}";
            ConnectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

            _keepAlive = new SqliteConnection(ConnectionString);
            _keepAlive.Open();
        }

        public string ConnectionString { get; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("TRACTO_MODE", "Connected");

            builder.ConfigureServices(services =>
            {
                // Replace DB context with isolated in-memory SQLite
                RemoveDbContextDescriptors(services);

                var dbOpts = new DbContextOptionsBuilder<OrchestratorDbContext>()
                    .UseSqlite(ConnectionString)
                    .ReplaceService<IModelCustomizer, DateTimeOffsetModelCustomizer>()
                    .Options;

                services.AddSingleton<IDbContextFactory<OrchestratorDbContext>>(
                    new OrchestratorDbContextFactory(dbOpts));

                services.AddScoped<OrchestratorDbContext>(sp =>
                    sp.GetRequiredService<IDbContextFactory<OrchestratorDbContext>>()
                      .CreateDbContext());

                // Replace OrchestratorSettings to inject the test HMAC key.
                // HmacValidator is Scoped and resolves OrchestratorSettings from DI,
                // so replacing the singleton here propagates to every request.
                var settingsDescriptors = services
                    .Where(d => d.ServiceType == typeof(OrchestratorSettings))
                    .ToList();
                foreach (var d in settingsDescriptors) services.Remove(d);

                services.AddSingleton(new OrchestratorSettings(
                    Mode: DeploymentMode.Connected,
                    HmacKeys: new Dictionary<string, string>
                    {
                        [TestKeyId] = Convert.ToBase64String(TestKeyBytes)
                    }));
            });
        }

        private static void RemoveDbContextDescriptors(IServiceCollection services)
        {
            var ctxType = typeof(OrchestratorDbContext);
            var toRemove = services
                .Where(d => IsRelated(d.ServiceType, ctxType) ||
                            (d.ImplementationType is not null && IsRelated(d.ImplementationType, ctxType)))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);
        }

        private static bool IsRelated(Type t, Type ctx)
        {
            if (t == ctx) return true;
            return t.IsGenericType && t.GenericTypeArguments.Any(a => a == ctx);
        }

        // Structurally-valid JWT accepted by JwtAuthMiddleware when no signing key is configured.
        private const string FakeJwt =
            "eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJ0ZXN0LXdvcmtlciJ9.";

        public HttpClient AuthenticatedClient()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {FakeJwt}");
            return client;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Environment.SetEnvironmentVariable("TRACTO_MODE", _previousMode);
                _keepAlive.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private readonly HmacCallbackTestFactory _factory;

    public OrchIt07_CallbackHmacTests(HmacCallbackTestFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Single test covering both HMAC AC steps:
    ///   1. Valid HMAC → POST /callbacks → 200 Accepted + Run B QUEUED.
    ///   2. Replay same EventId → POST /callbacks → 200 DUPLICATE + Run B count still 1.
    ///
    /// Both assertions are kept in one test to prevent the QUEUED Run B created in step 1
    /// from leaking into a second test's pull queue when tests share the same factory DB.
    /// </summary>
    [Fact]
    public async Task ValidHmacCallback_CreatesRunB_ThenDuplicateEventId_Returns200Duplicate()
    {
        var client   = _factory.AuthenticatedClient();
        var workerId = $"worker-cb-{Guid.NewGuid():N}";
        var scriptId = $"script-cb-{Guid.NewGuid():N}";
        var contId   = $"cont-cb-{Guid.NewGuid():N}";

        // Register worker and publish source + continuation artifacts
        await client.PostAsJsonAsync("/workers/register", new
        {
            workerId,
            runtimeType             = "dotnet",
            actionNames             = new[] { "Run" },
            capabilityDigest        = (string?)null,
            absenceThresholdSeconds = 60
        });

        await client.PutAsJsonAsync($"/artifacts/{scriptId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = Convert.ToBase64String("script-src"u8.ToArray()),
            signature       = (string?)null,
            selectorContent = (string?)null
        });

        await client.PutAsJsonAsync($"/artifacts/{contId}/versions/v1", new
        {
            schemaVersion   = "1",
            payload         = Convert.ToBase64String("script-cont"u8.ToArray()),
            signature       = (string?)null,
            selectorContent = (string?)null
        });

        // Create Run A
        var jobResp = await client.PostAsJsonAsync("/jobs", new
        {
            scriptArtifactId = scriptId,
            scriptVersion    = "v1",
            schemaVersion    = "1",
            inputRefs        = Array.Empty<string>(),
            secretRefs       = Array.Empty<string>(),
            triggerType      = "Manual"
        });
        jobResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var runAId = Guid.Parse((await ParseJsonAsync(jobResp)).GetProperty("runId").GetString()!);

        // Create continuation mapping so the callback creates Run B
        var mappingResp = await client.PostAsJsonAsync("/continuation-mappings", new
        {
            triggerRunId          = runAId,
            triggerState          = "HandedOff",
            targetArtifactId      = contId,
            targetArtifactVersion = "v1"
        });
        mappingResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Drive Run A to HANDED_OFF state via normal lease lifecycle
        var pullResp = await client.PostAsJsonAsync("/jobs/pull", new
        {
            workerId,
            runtimeType     = "dotnet",
            capabilityNames = Array.Empty<string>()
        });
        pullResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pullBody  = await ParseJsonAsync(pullResp);
        pullBody.GetProperty("result").GetString().Should().Be("JobOffered",
            "the pull must return the newly created job");
        var pulledRunId = Guid.Parse(pullBody.GetProperty("runId").GetString()!);
        pulledRunId.Should().Be(runAId, "the pull must offer the job we just created");
        var attemptId = Guid.Parse(pullBody.GetProperty("attemptId").GetString()!);

        var acceptResp = await client.PostAsJsonAsync($"/jobs/{runAId}/lease/accept", new { attemptId, workerId });
        acceptResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var resultResp = await client.PostAsJsonAsync($"/jobs/{runAId}/result", new
        {
            attemptId,
            terminalState = "HandedOff"
        });
        resultResp.StatusCode.Should().Be(HttpStatusCode.OK,
            "submitting HANDED_OFF result must succeed (200)");

        // Retrieve the run's CorrelationId from DB — /callbacks uses CorrelationId, not RunId
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var runA = await db.RunRecords.FirstAsync(r => r.Id == runAId);
        var correlationId = runA.CorrelationId;

        // ── Step 1: POST /callbacks with valid HMAC ───────────────────────────────
        // Canonical string: {keyId}|{timestamp}|{nonce}|{eventId}|{correlationId}|{payload}
        var keyId          = HmacCallbackTestFactory.TestKeyId;
        var keyBytes       = HmacCallbackTestFactory.TestKeyBytes;
        var eventId        = $"evt-{Guid.NewGuid():N}";
        var timestamp      = DateTimeOffset.UtcNow.ToString("O");
        var nonce          = Guid.NewGuid().ToString("N");
        var payloadContent = """{"status":"service-completed"}""";

        var canonical = $"{keyId}|{timestamp}|{nonce}|{eventId}|{correlationId}|{payloadContent}";
        var hmacBytes = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(canonical));
        var signature = Convert.ToHexString(hmacBytes).ToLowerInvariant(); // 64-char lowercase hex

        var resp1 = await _factory.CreateClient().SendAsync(BuildCallbackRequest(
            keyId, timestamp, nonce, signature, eventId, correlationId, payloadContent));
        resp1.StatusCode.Should().Be(HttpStatusCode.OK,
            "a validly HMAC-signed callback for a HANDED_OFF run must return 200");

        var body1 = await ParseJsonAsync(resp1);
        body1.GetProperty("outcome").GetString().Should().Be("Accepted",
            "the first valid callback for this run must be Accepted");

        // Verify Run B exists and is QUEUED.
        // ScopedContinuationQueue runs synchronously inside EnqueueAsync so Run B
        // is committed before /callbacks returns.
        var continuationRecords = await db.ContinuationRunRecords
            .Where(r => r.SourceRunId == runAId)
            .ToListAsync();
        continuationRecords.Should().HaveCount(1,
            "one Run B must be created by the /callbacks acceptance");

        var runBId = continuationRecords[0].RunBId;
        var runB = await db.RunRecords.FirstOrDefaultAsync(r => r.Id == runBId);
        runB.Should().NotBeNull("Run B must exist in RunRecords");
        runB!.State.Should().Be(Tracto.Orchestrator.Domain.Runs.RunState.Queued,
            "Run B created via /callbacks must start in QUEUED state");

        // ── Step 2: replay the same EventId → 200 DUPLICATE, Run B count unchanged ─
        var resp2 = await _factory.CreateClient().SendAsync(BuildCallbackRequest(
            keyId, timestamp, nonce, signature, eventId, correlationId, payloadContent));
        resp2.StatusCode.Should().Be(HttpStatusCode.OK,
            "replay of the same EventId must return 200 (not an error)");

        var body2 = await ParseJsonAsync(resp2);
        body2.GetProperty("outcome").GetString().Should().Be("Duplicate",
            "the second POST with the same EventId must be deduplicated (HMAC replay protection: EventId dedup)");

        // Run B count must remain at 1 — no second Run B created by the duplicate
        var continuationRecords2 = await db.ContinuationRunRecords
            .Where(r => r.SourceRunId == runAId)
            .ToListAsync();
        continuationRecords2.Should().HaveCount(1,
            "Run B count must stay at 1 after a duplicate /callbacks POST");
    }

    private static HttpRequestMessage BuildCallbackRequest(
        string keyId, string timestamp, string nonce, string signature,
        string eventId, Guid correlationId, string payload)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/callbacks");
        req.Headers.TryAddWithoutValidation("X-KeyId",          keyId);
        req.Headers.TryAddWithoutValidation("X-Timestamp",      timestamp);
        req.Headers.TryAddWithoutValidation("X-Nonce",          nonce);
        req.Headers.TryAddWithoutValidation("X-Hmac-Signature", signature);
        req.Headers.TryAddWithoutValidation("Authorization",
            "Bearer eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiJ0ZXN0LXdvcmtlciJ9.");
        req.Content = JsonContent.Create(new
        {
            eventId       = eventId,
            correlationId = correlationId,
            payload       = payload
        });
        return req;
    }

    private static async Task<JsonElement> ParseJsonAsync(HttpResponseMessage r)
    {
        var content = await r.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content).RootElement;
    }
}
