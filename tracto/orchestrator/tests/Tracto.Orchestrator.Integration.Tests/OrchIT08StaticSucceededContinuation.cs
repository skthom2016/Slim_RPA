#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Domain.Runs;
using Tracto.Orchestrator.Infrastructure.Persistence;
using Tracto.Orchestrator.Integration.Tests.Shared;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-08: static SUCCEEDED continuation fires exactly once.
///
/// AC:
///   Create static ContinuationMapping (TriggerState=SUCCEEDED, TriggerRunId=null).
///   Complete run SUCCEEDED. Assert one Run B QUEUED.
///   Complete another run with FAILED → no Run B (no global FAILED mapping).
///   Complete first run-type SUCCEEDED again → still one Run B (dedup via ContinuationRunRecord).
/// </summary>
public sealed class OrchIt08StaticSucceededContinuation
    : IClassFixture<OrchIt08StaticSucceededContinuation.Factory>
{
    public sealed class Factory : OrchestratorTestFactory { }

    private readonly Factory    _factory;
    private readonly HttpClient _client;

    public OrchIt08StaticSucceededContinuation(Factory factory)
    {
        _factory = factory;
        _client  = factory.AuthenticatedClient();
    }

    // Helper: runs a job to completion with the given terminal state via HTTP.
    private async Task<(Guid runId, Guid runningAttemptId)> DriveToRunningAsync(
        string artifactId, string artifactVersion, string workerSuffix)
    {
        // Publish artifact (409 if already exists — acceptable).
        var artResp = await _client.PutAsJsonAsync(
            $"/artifacts/{artifactId}/versions/{artifactVersion}",
            new
            {
                schemaVersion = "1",
                payload       = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("content")),
                signature     = "sig-ok",
                selectorContent = (string?)null
            });
        if (!artResp.IsSuccessStatusCode && (int)artResp.StatusCode != 409)
            artResp.EnsureSuccessStatusCode();

        await ApiHelpers.RegisterWorkerAsync(_client, $"worker-oc08-{workerSuffix}", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, artifactId, artifactVersion);

        var pullJson = await ApiHelpers.PullJobAsync(_client, $"worker-oc08-{workerSuffix}", "dotnet");
        var attemptId   = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();
        var leaseJson   = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, $"worker-oc08-{workerSuffix}");
        return (offeredRunId, leaseJson.GetProperty("attemptId").GetGuid());
    }

    [Fact]
    public async Task GlobalSucceededMapping_TriggersRunB_OnFirstSucceeded()
    {
        // Arrange — publish both artifacts.
        await ApiHelpers.PublishArtifactAsync(_client, "art-a-oc08", "1.0");
        await ApiHelpers.PublishArtifactAsync(_client, "art-b-oc08", "2.0");

        // Create a GLOBAL continuation mapping (TriggerRunId=null, TriggerState=SUCCEEDED).
        var mapResp = await _client.PostAsJsonAsync("/continuation-mappings", new
        {
            triggerRunId          = (Guid?)null,
            triggerState          = "Succeeded",
            targetArtifactId      = "art-b-oc08",
            targetArtifactVersion = "2.0",
            inputTemplate         = (string?)null
        });
        mapResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Drive run A to RUNNING.
        var (runId, runningAttemptId) = await DriveToRunningAsync("art-a-oc08", "1.0", "a1");

        // Act — complete Run A as SUCCEEDED.
        await ApiHelpers.CommitResultAsync(_client, runId, runningAttemptId, "Succeeded");

        // Trigger static continuation (equivalent to what StaticSucceededContinuationService does).
        using var scope   = _factory.Services.CreateScope();
        var continuationService = scope.ServiceProvider.GetRequiredService<StaticSucceededContinuationService>();
        await continuationService.OnSucceededAsync(runId, businessJobId: null, CancellationToken.None);

        // Assert — one Run B is QUEUED.
        var db    = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var runBRecord = await db.ContinuationRunRecords
            .FirstOrDefaultAsync(cr => cr.SourceRunId == runId && cr.TriggerState == RunState.Succeeded);
        runBRecord.Should().NotBeNull("Run B continuation record must exist");

        var runB = await db.RunRecords.FindAsync(runBRecord!.RunBId);
        runB.Should().NotBeNull();
        runB!.State.Should().Be(RunState.Queued, "Run B must be QUEUED");
    }

    [Fact]
    public async Task GlobalSucceededMapping_CalledTwice_DeduplicatesRunB()
    {
        // Arrange.
        await ApiHelpers.PublishArtifactAsync(_client, "art-a2-oc08", "1.0");
        await ApiHelpers.PublishArtifactAsync(_client, "art-b2-oc08", "2.0");

        await _client.PostAsJsonAsync("/continuation-mappings", new
        {
            triggerRunId          = (Guid?)null,
            triggerState          = "Succeeded",
            targetArtifactId      = "art-b2-oc08",
            targetArtifactVersion = "2.0",
            inputTemplate         = (string?)null
        });

        var (runId, runningAttemptId) = await DriveToRunningAsync("art-a2-oc08", "1.0", "b1");
        await ApiHelpers.CommitResultAsync(_client, runId, runningAttemptId, "Succeeded");

        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<StaticSucceededContinuationService>();

        // Act — trigger OnSucceeded twice.
        await svc.OnSucceededAsync(runId, businessJobId: null);
        await svc.OnSucceededAsync(runId, businessJobId: null);

        // Assert — still only one ContinuationRunRecord.
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var count = await db.ContinuationRunRecords
            .CountAsync(cr => cr.SourceRunId == runId && cr.TriggerState == RunState.Succeeded);
        count.Should().Be(1, because: "OnSucceeded called twice must not create duplicate Run B");
    }

    [Fact]
    public async Task GlobalSucceededMapping_FailedRun_DoesNotCreateRunB()
    {
        // Arrange — only SUCCEEDED mapping exists; run completes as FAILED.
        await ApiHelpers.PublishArtifactAsync(_client, "art-a3-oc08", "1.0");
        await ApiHelpers.PublishArtifactAsync(_client, "art-b3-oc08", "2.0");

        await _client.PostAsJsonAsync("/continuation-mappings", new
        {
            triggerRunId          = (Guid?)null,
            triggerState          = "Succeeded",
            targetArtifactId      = "art-b3-oc08",
            targetArtifactVersion = "2.0",
            inputTemplate         = (string?)null
        });

        var (runId, runningAttemptId) = await DriveToRunningAsync("art-a3-oc08", "1.0", "c1");
        await ApiHelpers.CommitResultAsync(_client, runId, runningAttemptId, "Failed");

        // Act — trigger continuation with FAILED (no FAILED mapping exists).
        using var scope = _factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateContinuationRunCommandHandler>();
        var result  = await handler.HandleAsync(
            new CreateContinuationRunCommand(runId, RunState.Failed), CancellationToken.None);

        // Assert — no Run B created (no FAILED mapping).
        result.IsSuccess.Should().BeTrue();
        result.Value.RunBId.Should().BeNull("no Run B for FAILED when only SUCCEEDED mapping exists");

        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var count = await db.ContinuationRunRecords.CountAsync(cr => cr.SourceRunId == runId);
        count.Should().Be(0);
    }
}
