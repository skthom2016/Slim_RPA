#nullable enable

using System.Collections.Generic;

namespace Tracto.Mock.Orchestrator;

/// <summary>Represents a job enqueued in the mock orchestrator for dispatch to a matching worker.</summary>
internal sealed record MockJob(
    string RunId,
    string ScriptRuntimeType,
    int RequiredSchemaMajor,
    IReadOnlyList<string> RequiredNamespaces,
    bool IsInteractive,
    string ArtifactUrl,
    string ArtifactDigest,
    string JobSchemaVersion,
    int LeaseDurationMs = 60_000);
