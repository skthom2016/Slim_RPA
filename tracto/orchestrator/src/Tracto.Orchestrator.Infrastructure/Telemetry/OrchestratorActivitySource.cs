#nullable enable
using System.Diagnostics;

namespace Tracto.Orchestrator.Infrastructure.Telemetry;

/// <summary>
/// Central <see cref="ActivitySource"/> for the Tracto Orchestrator.
///
/// All command-handler spans are started from this source so that the OTEL SDK
/// can route them to registered exporters (Jaeger, OTLP, Console, etc.).
///
/// Usage in a command handler:
/// <code>
///     using var activity = OrchestratorActivitySource.StartActivity("CreateRun");
///     activity?.SetTag("run.id",     runId.ToString());
///     activity?.SetTag("attempt.id", attemptId.ToString());
/// </code>
///
/// The source is intentionally a singleton (static) because ActivitySource instances
/// are thread-safe and cheap; creating one per handler would fragment the trace hierarchy.
/// </summary>
public static class OrchestratorActivitySource
{
    /// <summary>
    /// Canonical name reported to the OTEL collector. Must match the service name
    /// configured in the OTEL SDK setup in Program.cs.
    /// </summary>
    public const string SourceName = "Tracto.Orchestrator";

    /// <summary>
    /// Version reported to the OTEL collector. Align with the assembly version.
    /// </summary>
    public const string SourceVersion = "1.0.0";

    private static readonly ActivitySource _source =
        new(SourceName, SourceVersion);

    /// <summary>
    /// Starts a new <see cref="Activity"/> for <paramref name="operationName"/>.
    /// Returns null when no listener is registered (zero-allocation fast path).
    /// </summary>
    public static Activity? StartActivity(string operationName, ActivityKind kind = ActivityKind.Internal)
        => _source.StartActivity(operationName, kind);

    /// <summary>
    /// Exposes the underlying <see cref="ActivitySource"/> for registration with the
    /// OTEL SDK via <c>AddSource(OrchestratorActivitySource.SourceName)</c>.
    /// </summary>
    public static ActivitySource Source => _source;
}
