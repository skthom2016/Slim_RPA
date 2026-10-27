namespace Tracto.Desktop.Recovery;

/// <summary>
/// Test double for <see cref="IHeartbeatSender"/>.
/// Records every <see cref="SendHeartbeatAsync"/> call; inspect <see cref="CallCount"/> to assert heartbeat activity.
/// </summary>
public sealed class MockHeartbeatSender : IHeartbeatSender
{
    private int _callCount;

    /// <summary>Gets the number of times <see cref="SendHeartbeatAsync"/> has been called.</summary>
    public int CallCount => _callCount;

    /// <inheritdoc />
    public Task SendHeartbeatAsync(string leaseId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        System.Threading.Interlocked.Increment(ref _callCount);
        return Task.CompletedTask;
    }
}
