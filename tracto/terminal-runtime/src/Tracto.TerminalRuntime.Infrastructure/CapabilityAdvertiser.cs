using Microsoft.Extensions.Options;
using Tracto.TerminalRuntime.Application;
using Tracto.TerminalRuntime.Application.Common;
using Tracto.TerminalRuntime.Domain;

namespace Tracto.TerminalRuntime.Infrastructure;

/// <summary>
/// Builds the WorkerCapabilityAdvertisement from configuration.
///
/// Invariants enforced here (CEC C4):
///   - RuntimeType is always "terminal"
///   - HeadlessOnly is always true; the worker never requires an interactive session
///   - InteractiveSessionId is always null; no foreground input lease is held
/// </summary>
public sealed class CapabilityAdvertiser : ICapabilityAdvertiser
{
    private readonly CapabilityAdvertiserOptions _options;
    private readonly IClock _clock;

    public CapabilityAdvertiser(IOptions<CapabilityAdvertiserOptions> options, IClock clock)
    {
        _options = options.Value;
        _clock   = clock;
    }

    public Result<WorkerCapabilityAdvertisement, Error> BuildAdvertisement()
    {
        if (string.IsNullOrWhiteSpace(_options.WorkerId))
            return Result<WorkerCapabilityAdvertisement, Error>.Fail(
                Error.Validation("CAP_001", "WorkerId must not be empty."));

        if (_options.SupportedShells is not { Count: > 0 })
            return Result<WorkerCapabilityAdvertisement, Error>.Fail(
                Error.Validation("CAP_002", "At least one shell must be configured."));

        var advertisement = new WorkerCapabilityAdvertisement(
            RuntimeType:          WorkerCapabilityAdvertisement.TerminalRuntimeType,
            WorkerId:             _options.WorkerId,
            SupportedShells:      _options.SupportedShells.AsReadOnly(),
            WorkerVersion:        _options.WorkerVersion,
            HeadlessOnly:         true,    // invariant: always true
            InteractiveSessionId: null,    // invariant: never hold an interactive lease
            AdvertisedAt:         _clock.UtcNow);

        return Result<WorkerCapabilityAdvertisement, Error>.Ok(advertisement);
    }
}

public sealed class CapabilityAdvertiserOptions
{
    public string WorkerId              { get; set; } = string.Empty;
    public List<string> SupportedShells { get; set; } = ["cmd", "powershell"];
    public string WorkerVersion         { get; set; } = "1.0.0";
}
