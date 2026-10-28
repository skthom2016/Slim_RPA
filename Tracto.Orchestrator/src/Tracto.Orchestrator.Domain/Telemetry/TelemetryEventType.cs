#nullable enable
namespace Tracto.Orchestrator.Domain.Telemetry;

/// <summary>
/// Canonical string constants for every telemetry event type emitted by the orchestrator.
/// Using string constants (not an enum) preserves forward compatibility when new event types
/// are added without requiring schema migrations on the consuming side.
/// </summary>
public static class TelemetryEventType
{
    // ── Run lifecycle ──────────────────────────────────────────────────────────

    /// <summary>A new automation run was accepted and persisted in CREATED state.</summary>
    public const string RunCreated = "RUN_CREATED";

    /// <summary>The run transitioned from CREATED to QUEUED.</summary>
    public const string RunQueued = "RUN_QUEUED";

    /// <summary>A worker claimed the run; state moved to LEASED.</summary>
    public const string RunLeased = "RUN_LEASED";

    /// <summary>The worker started execution; state moved to RUNNING.</summary>
    public const string RunRunning = "RUN_RUNNING";

    /// <summary>The run completed successfully; state moved to SUCCEEDED.</summary>
    public const string RunSucceeded = "RUN_SUCCEEDED";

    /// <summary>The run completed with a failure; state moved to FAILED.</summary>
    public const string RunFailed = "RUN_FAILED";

    /// <summary>The run was handed off to another system; state moved to HANDED_OFF.</summary>
    public const string RunHandedOff = "RUN_HANDED_OFF";

    /// <summary>The run was explicitly cancelled; state moved to CANCELLED.</summary>
    public const string RunCancelled = "RUN_CANCELLED";

    /// <summary>The run exceeded its deadline; state moved to TIMED_OUT.</summary>
    public const string RunTimedOut = "RUN_TIMED_OUT";

    /// <summary>
    /// The run entered an indeterminate terminal state; state moved to UNKNOWN.
    /// NeedsAttention=true is always set alongside this event.
    /// UNKNOWN is a state — not an errorCategory — and is never auto-replayed.
    /// </summary>
    public const string RunUnknown = "RUN_UNKNOWN";

    // ── Worker lifecycle ───────────────────────────────────────────────────────

    /// <summary>A worker registered its capabilities with the orchestrator.</summary>
    public const string WorkerRegistered = "WORKER_REGISTERED";

    /// <summary>A worker went offline (heartbeat expired or explicit deregistration).</summary>
    public const string WorkerOffline = "WORKER_OFFLINE";

    // ── Callback events ────────────────────────────────────────────────────────

    /// <summary>An external callback payload was received and authenticated successfully.</summary>
    public const string CallbackReceived = "CALLBACK_RECEIVED";

    /// <summary>
    /// An external callback failed HMAC authentication.
    /// Metadata includes sourceIp; callback body is NEVER included (security invariant 4).
    /// </summary>
    public const string CallbackAuthFailed = "CALLBACK_AUTH_FAILED";

    // ── Security events ────────────────────────────────────────────────────────

    /// <summary>
    /// A security-relevant event occurred (e.g. secret scan violation, unauthorized access attempt).
    /// No plaintext secrets are ever stored in metadata — store "REDACTED" for any secret refs.
    /// </summary>
    public const string SecurityEvent = "SECURITY_EVENT";

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all known event type constants in definition order.
    /// Useful for enumeration in tests that verify coverage.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        RunCreated, RunQueued, RunLeased, RunRunning,
        RunSucceeded, RunFailed, RunHandedOff, RunCancelled,
        RunTimedOut, RunUnknown,
        WorkerRegistered, WorkerOffline,
        CallbackReceived, CallbackAuthFailed,
        SecurityEvent
    ];
}
