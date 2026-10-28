using TractoRuntime.Browser.Contracts;

namespace TractoRuntime.Browser.Lease;

public interface ILeaseManager
{
    Task<LeaseAcquireResult> AcquireAsync(LeaseAcquireRequest request, CancellationToken ct);
    Task RenewAsync(HeartbeatRequest request, CancellationToken ct);
    Task ReleaseAsync(string leaseToken, CancellationToken ct);
}
