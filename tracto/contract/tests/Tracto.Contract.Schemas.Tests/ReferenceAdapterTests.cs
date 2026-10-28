#nullable enable

using System.Threading.Tasks;
using FluentAssertions;
using Tracto.Contract.Models;
using Tracto.Contract.Testing;
using Tracto.Mock.Orchestrator;
using Tracto.Reference.Adapter;
using Xunit;

namespace Tracto.Contract.Schemas.Tests;

/// <summary>
/// ST1-15-AC08: Reference adapter exercises the full round-trip using only interfaces,
/// not concrete orchestrator types. Compile-time enforcement is verified in
/// <see cref="ReleaseGovernanceTests.ReferenceAdapter_DependsOnInterface_NotConcrete"/>.
/// </summary>
public sealed class ReferenceAdapterTests
{
    // -----------------------------------------------------------------------
    // ST1-15-AC08: Round-trip with a desktop worker — full lifecycle succeeds
    // -----------------------------------------------------------------------
    [Fact]
    public async Task RoundTrip_DesktopWorker_Succeeds()
    {
        var clock = new FakeTimeProvider();
        var adapter = new ReferenceAdapter(clock);
        var mock = new MockOrchestrator(clock: clock);

        // Enqueue a job BEFORE the adapter runs (the adapter does not own the queue).
        mock.EnqueueJob(new MockJob(
            RunId: "run-ref-01",
            ScriptRuntimeType: "desktop",
            RequiredSchemaMajor: 1,
            RequiredNamespaces: new[] { "desktop.*" },
            IsInteractive: true,
            ArtifactUrl: "https://example.com/a.zip",
            ArtifactDigest: "sha256:abc",
            JobSchemaVersion: "1.0.0"));

        var result = await adapter.ExecuteRoundTripAsync(mock, "ref-w1");

        result.Succeeded.Should().BeTrue(because: result.FailureReason ?? "");
        result.FinalState.Should().Be(RunState.Succeeded);
    }

    // -----------------------------------------------------------------------
    // ST1-15-AC08: When no job is available the adapter returns a failure result
    // -----------------------------------------------------------------------
    [Fact]
    public async Task RoundTrip_NoJob_ReturnsFailure()
    {
        var adapter = new ReferenceAdapter();
        var mock = new MockOrchestrator();

        // Do NOT enqueue any job — the pull must return NoPendingJob.
        var result = await adapter.ExecuteRoundTripAsync(mock, "ref-w2");

        result.Succeeded.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }
}
