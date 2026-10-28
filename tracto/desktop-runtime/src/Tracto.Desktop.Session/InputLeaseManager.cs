namespace Tracto.Desktop.Session;

/// <summary>
/// Manages an exclusive input lease for the current desktop session.
/// Only one job may hold the lease at a time; concurrent attempts are safely rejected.
/// </summary>
public sealed class InputLeaseManager : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private volatile string? _currentHolder;

    /// <summary>Gets whether the lease is currently held by any job.</summary>
    public bool IsLeaseHeld => _currentHolder is not null;

    /// <summary>
    /// Attempts to acquire the exclusive input lease for the given <paramref name="jobId"/>.
    /// This method is thread-safe and async-safe.
    /// </summary>
    /// <param name="jobId">Identifier of the job requesting the lease.</param>
    /// <returns>
    /// <c>true</c> if the lease was acquired; <c>false</c> if it is already held by another job.
    /// </returns>
    public bool TryAcquireLease(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job ID must not be null or whitespace.", nameof(jobId));

        // Non-blocking attempt.
        if (!_semaphore.Wait(0))
            return false;

        _currentHolder = jobId;
        return true;
    }

    /// <summary>
    /// Releases the input lease held by <paramref name="jobId"/>.
    /// No-op if the lease is not held or is held by a different job ID.
    /// </summary>
    /// <param name="jobId">Identifier of the job releasing the lease.</param>
    public void ReleaseLease(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job ID must not be null or whitespace.", nameof(jobId));

        if (_currentHolder == jobId)
        {
            _currentHolder = null;
            _semaphore.Release();
        }
    }

    /// <summary>Releases all resources, including the semaphore.</summary>
    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
