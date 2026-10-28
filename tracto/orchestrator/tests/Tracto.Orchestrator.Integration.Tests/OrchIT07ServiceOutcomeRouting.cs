#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tracto.Orchestrator.Application.Abstractions;
using Tracto.Orchestrator.Application.Jobs;
using Tracto.Orchestrator.Infrastructure.Persistence;
using Tracto.Orchestrator.Integration.Tests.Shared;

namespace Tracto.Orchestrator.Integration.Tests;

/// <summary>
/// ORCH-IT-07: serviceOutcome routing and single Run B.
///
/// AC:
///   HANDED_OFF run. Create ContinuationMapping (TriggerRunId + TriggerState=HANDED_OFF).
///   Trigger continuation via CreateContinuationRunCommandHandler (application layer).
///   Assert Run B QUEUED. Trigger again (same SourceRunId) → deduplication. Assert still one Run B.
///
/// Note: POST /callbacks endpoint is not wired in Program.cs (OC-07 async gateway).
///       This test exercises the continuation service layer directly through DI,
///       which is the mechanism that /callbacks would call after HMAC validation.
/// </summary>
public sealed class OrchIt07ServiceOutcomeRouting
    : IClassFixture<OrchIt07ServiceOutcomeRouting.Factory>
{
    public sealed class Factory : OrchestratorTestFactory { }

    private readonly Factory    _factory;
    private readonly HttpClient _client;

    public OrchIt07ServiceOutcomeRouting(Factory factory)
    {
        _factory = factory;
        _client  = factory.AuthenticatedClient();
    }

    [Fact]
    public async Task HandedOffRun_WithContinuationMapping_CreatesRunB()
    {
        // Arrange — drive run to HANDED_OFF.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc07a", "1.0");
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc07b", "2.0");   // Run B script
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc07a", "dotnet");

        var jobJson = await ApiHelpers.CreateJobAsync(_client, "art-oc07a", "1.0");
        var sourceRunId = jobJson.GetProperty("runId").GetGuid();

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc07a", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();

        var leaseJson = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc07a");
        var runningAttemptId = leaseJson.GetProperty("attemptId").GetGuid();

        // Create a continuation mapping for this specific run.
        var mappingResp = await _client.PostAsJsonAsync("/continuation-mappings", new
        {
            triggerRunId          = offeredRunId,
            triggerState          = "HandedOff",
            targetArtifactId      = "art-oc07b",
            targetArtifactVersion = "2.0",
            inputTemplate         = (string?)null
        });
        mappingResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Worker submits HANDED_OFF result.
        await ApiHelpers.CommitResultAsync(_client, offeredRunId, runningAttemptId, "HandedOff");

        // Act — trigger continuation via application service (equivalent to /callbacks handler).
        using var scope = _factory.Services.CreateScope();
        var continuationHandler = scope.ServiceProvider
            .GetRequiredService<CreateContinuationRunCommandHandler>();

        var continuationResult = await continuationHandler.HandleAsync(
            new CreateContinuationRunCommand(
                SourceRunId:  offeredRunId,
                TriggerState: Tracto.Orchestrator.Domain.Runs.RunState.HandedOff,
                BusinessJobId: null),
            CancellationToken.None);

        // Assert — Run B was created.
        continuationResult.IsSuccess.Should().BeTrue();
        continuationResult.Value.RunBId.Should().NotBeNull("Run B must be created for HANDED_OFF with mapping");

        var runBId = continuationResult.Value.RunBId!.Value;

        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var runB = await db.RunRecords.FindAsync(runBId);
        runB.Should().NotBeNull();
        runB!.State.Should().Be(Tracto.Orchestrator.Domain.Runs.RunState.Queued,
            because: "continuation Run B must be QUEUED immediately");
    }

    [Fact]
    public async Task HandedOffRun_TriggerContinuationTwice_OnlyOneRunBCreated()
    {
        // Arrange — drive run to HANDED_OFF.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc07c", "1.0");
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc07d", "3.0");
        await ApiHelpers.RegisterWorkerAsync(_client, "worker-oc07c", "dotnet");
        await ApiHelpers.CreateJobAsync(_client, "art-oc07c", "1.0");

        var pullJson = await ApiHelpers.PullJobAsync(_client, "worker-oc07c", "dotnet");
        var attemptId = pullJson.GetProperty("attemptId").GetGuid();
        var offeredRunId = pullJson.GetProperty("runId").GetGuid();
        var leaseJson = await ApiHelpers.AcceptLeaseAsync(_client, offeredRunId, attemptId, "worker-oc07c");
        var runningAttemptId = leaseJson.GetProperty("attemptId").GetGuid();

        // Create continuation mapping.
        await _client.PostAsJsonAsync("/continuation-mappings", new
        {
            triggerRunId          = offeredRunId,
            triggerState          = "HandedOff",
            targetArtifactId      = "art-oc07d",
            targetArtifactVersion = "3.0",
            inputTemplate         = (string?)null
        });

        await ApiHelpers.CommitResultAsync(_client, offeredRunId, runningAttemptId, "HandedOff");

        // Act — trigger continuation twice (simulating duplicate callback delivery).
        using var scope = _factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateContinuationRunCommandHandler>();
        var cmd = new CreateContinuationRunCommand(offeredRunId, Tracto.Orchestrator.Domain.Runs.RunState.HandedOff);

        var r1 = await handler.HandleAsync(cmd, CancellationToken.None);
        var r2 = await handler.HandleAsync(cmd, CancellationToken.None);

        // Assert — both succeed but the second is a dedup.
        r1.IsSuccess.Should().BeTrue();
        r2.IsSuccess.Should().BeTrue();
        r2.Value.AlreadyExisted.Should().BeTrue("second continuation trigger must be deduplicated");

        // Only one ContinuationRunRecord exists.
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var count = await db.ContinuationRunRecords
            .CountAsync(cr => cr.SourceRunId == offeredRunId);
        count.Should().Be(1, because: "duplicate continuation trigger must not create multiple Run B records");
    }

    [Fact]
    public async Task ContinuationMapping_Created_CanBeRetrievedById()
    {
        // Arrange — publish target artifact.
        await ApiHelpers.PublishArtifactAsync(_client, "art-oc07e", "1.0");

        // Act — create a continuation mapping.
        var createResp = await _client.PostAsJsonAsync("/continuation-mappings", new
        {
            triggerRunId          = (Guid?)null,
            triggerState          = "Succeeded",
            targetArtifactId      = "art-oc07e",
            targetArtifactVersion = "1.0",
            inputTemplate         = "{\"key\":\"value\"}"
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var mappingId  = createBody.GetProperty("mappingId").GetGuid();

        // Assert — can retrieve the mapping.
        var getResp  = await _client.GetAsync($"/continuation-mappings/{mappingId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var getBody  = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        getBody.GetProperty("mappingId").GetGuid().Should().Be(mappingId);
        getBody.GetProperty("triggerState").GetString().Should().Be("SUCCEEDED");
        getBody.GetProperty("targetArtifactId").GetString().Should().Be("art-oc07e");
    }
}
