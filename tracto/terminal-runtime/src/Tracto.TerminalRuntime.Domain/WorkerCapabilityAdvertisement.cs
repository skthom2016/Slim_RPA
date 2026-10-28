namespace Tracto.TerminalRuntime.Domain;

/// <summary>
/// Immutable capability advertisement broadcast by a Terminal worker.
///
/// Key invariants (CEC C4):
///   - RuntimeType must be "terminal"
///   - HeadlessOnly = true  →  worker does NOT require an interactive Windows session
///   - InteractiveSessionId must be null  →  no foreground desktop input lease is held
/// </summary>
public sealed record WorkerCapabilityAdvertisement(
    string RuntimeType,
    string WorkerId,
    IReadOnlyList<string> SupportedShells,
    string WorkerVersion,
    bool HeadlessOnly,
    string? InteractiveSessionId,
    DateTimeOffset AdvertisedAt)
{
    public const string TerminalRuntimeType = "terminal";
}
