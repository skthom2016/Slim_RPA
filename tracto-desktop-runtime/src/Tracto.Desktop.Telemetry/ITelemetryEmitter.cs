namespace Tracto.Desktop.Telemetry;

/// <summary>
/// Contract for emitting step telemetry records to an observability sink.
/// </summary>
public interface ITelemetryEmitter
{
    /// <summary>
    /// Emit a step telemetry record asynchronously.
    /// Implementations must not write any secret values; the record itself guarantees this
    /// by its type constraints, but implementations should additionally run a secret-pattern check.
    /// </summary>
    /// <param name="record">The telemetry record to emit.</param>
    /// <param name="ct">Cancellation token.</param>
    Task EmitAsync(StepTelemetryRecord record, CancellationToken ct);
}
