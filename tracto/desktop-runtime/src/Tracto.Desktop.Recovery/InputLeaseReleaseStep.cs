using Tracto.Desktop.Session;

namespace Tracto.Desktop.Recovery;

/// <summary>
/// Release step that returns the exclusive input lease to the
/// <see cref="InputLeaseManager"/> so subsequent runs can acquire it (DT-17-AC04).
/// </summary>
public sealed class InputLeaseReleaseStep : IResourceReleaseStep
{
    private readonly InputLeaseManager _leaseManager;

    /// <param name="leaseManager">
    ///   The shared <see cref="InputLeaseManager"/> for this desktop session.
    /// </param>
    public InputLeaseReleaseStep(InputLeaseManager leaseManager)
    {
        ArgumentNullException.ThrowIfNull(leaseManager);
        _leaseManager = leaseManager;
    }

    /// <inheritdoc/>
    public Task ExecuteAsync(CancellationContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        _leaseManager.ReleaseLease(ctx.JobId);

        return Task.CompletedTask;
    }
}
