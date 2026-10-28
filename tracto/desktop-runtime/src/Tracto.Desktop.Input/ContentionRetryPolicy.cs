namespace Tracto.Desktop.Input;

/// <summary>
/// Retries <see cref="IClipboardApiProvider.TryOpenClipboard"/> with a configurable
/// number of attempts and a delay between each, absorbing transient clipboard contention.
/// This policy is TRANSIENT_SAFE: it only retries the open attempt, not any consequential action.
/// </summary>
public sealed class ContentionRetryPolicy
{
    private readonly IClipboardApiProvider _provider;
    private readonly int _maxAttempts;
    private readonly int _delayMs;

    /// <summary>
    /// Initialises the retry policy.
    /// </summary>
    /// <param name="provider">The clipboard API provider to use.</param>
    /// <param name="maxAttempts">Maximum number of open attempts (must be &gt;= 1).</param>
    /// <param name="delayMs">Milliseconds to wait between failed attempts.</param>
    public ContentionRetryPolicy(IClipboardApiProvider provider, int maxAttempts = 5, int delayMs = 100)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Must be at least 1.");
        if (delayMs < 0)     throw new ArgumentOutOfRangeException(nameof(delayMs), "Must be non-negative.");

        _provider    = provider;
        _maxAttempts = maxAttempts;
        _delayMs     = delayMs;
    }

    /// <summary>
    /// Attempts to open the clipboard up to <c>maxAttempts</c> times, waiting <c>delayMs</c>
    /// between consecutive failures.
    /// </summary>
    /// <param name="owner">Window handle to pass to <see cref="IClipboardApiProvider.TryOpenClipboard"/>.</param>
    /// <param name="ct">Token that cancels the retry loop without opening the clipboard.</param>
    /// <returns><c>true</c> when the clipboard was opened; <c>false</c> when all attempts were exhausted.</returns>
    public async Task<bool> TryOpenAsync(IntPtr owner, CancellationToken ct = default)
    {
        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (_provider.TryOpenClipboard(owner))
                return true;

            if (attempt < _maxAttempts && _delayMs > 0)
                await Task.Delay(_delayMs, ct).ConfigureAwait(false);
        }

        return false;
    }
}
