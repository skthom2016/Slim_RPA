namespace Tracto.Desktop.Recovery;

/// <summary>
/// Thread-safe attempt fence that prevents any further automation actions once an UNKNOWN
/// outcome has been declared for the current execution attempt.
/// </summary>
/// <remarks>
/// Per DT-16-AC06:
/// <list type="bullet">
///   <item>
///     <see cref="TryFence"/> returns <see langword="true"/> on the first call — the caller may
///     proceed to record the UNKNOWN outcome.
///   </item>
///   <item>
///     Every subsequent call to <see cref="TryFence"/> returns <see langword="false"/>.
///   </item>
///   <item>
///     After the fence is set, <see cref="ThrowIfFenced"/> raises <see cref="AttemptFencedException"/>
///     for <em>all</em> subsequent actions — including read-only queries; there is no bypass path.
///   </item>
/// </list>
/// </remarks>
public sealed class AttemptFenceState
{
    private volatile bool _fenced;
    private string? _fencedAttemptId;
    private readonly object _lock = new();

    /// <summary>Gets whether the fence has been set.</summary>
    public bool IsFenced => _fenced;

    /// <summary>Gets the attempt identifier that raised the fence, or <see langword="null"/> if not yet fenced.</summary>
    public string? FencedAttemptId => _fencedAttemptId;

    /// <summary>
    /// Attempts to set the fence for <paramref name="attemptId"/>.
    /// </summary>
    /// <param name="attemptId">The execution attempt identifier to fence.</param>
    /// <returns>
    /// <see langword="true"/> if this is the first call and the fence was successfully set;
    /// <see langword="false"/> if the fence was already set by a prior call.
    /// </returns>
    public bool TryFence(string attemptId)
    {
        // Fast-path: already fenced — avoid lock acquisition.
        if (_fenced)
            return false;

        lock (_lock)
        {
            if (_fenced)
                return false;

            _fencedAttemptId = attemptId;
            _fenced = true;
            return true;
        }
    }

    /// <summary>
    /// Throws <see cref="AttemptFencedException"/> when the fence has been set.
    /// Must be called at the entry point of every action (including read-only queries)
    /// to enforce the "no continuation after UNKNOWN" contract.
    /// </summary>
    /// <exception cref="AttemptFencedException">
    /// Thrown when <see cref="IsFenced"/> is <see langword="true"/>.
    /// </exception>
    public void ThrowIfFenced()
    {
        if (_fenced)
            throw new AttemptFencedException(_fencedAttemptId ?? string.Empty);
    }
}
