namespace TractoRuntime.Browser.Session;

/// <summary>
/// Raised when a browser session expires mid-run and the runtime cannot automatically
/// re-authenticate (BRW-US-014 AC#6, WEB-009, BRE-006).
///
/// <para><b>Semantics (AC#6):</b> The runtime does not attempt to silently re-authenticate
/// using stored credentials.  The run stops either with <c>needsAttention=true</c> (when the
/// expired session requires operator intervention to provision a new credential) or as a
/// classified FAILED (when re-authentication is not applicable for this run type).</para>
///
/// <para>The <see cref="SessionRef"/> property holds only the opaque secret reference, never
/// the raw token or JSON value — consistent with BRE-006 and AC#2.</para>
/// </summary>
public sealed class SessionExpiryException : Exception
{
    /// <summary>
    /// The opaque signed session reference that has expired.
    /// This is an opaque identifier only — the raw storageState value is never held here.
    /// </summary>
    public string? SessionRef { get; }

    /// <summary>
    /// When <see langword="true"/>, the operator must be notified (<c>needsAttention=true</c>)
    /// so a fresh authenticated session can be provisioned before the run is retried.
    /// When <see langword="false"/>, the run terminates as a classified FAILED outcome
    /// without requiring operator attention.
    /// </summary>
    public bool NeedsAttention { get; }

    /// <summary>
    /// Initialises a new <see cref="SessionExpiryException"/> without an inner exception.
    /// </summary>
    /// <param name="message">Human-readable description of the expiry condition.</param>
    /// <param name="sessionRef">Opaque session reference (never a raw token value).</param>
    /// <param name="needsAttention">
    /// <see langword="true"/> to signal the operator; <see langword="false"/> for a plain
    /// FAILED outcome.  Defaults to <see langword="true"/> because most expiry cases require
    /// a human to provision a fresh credential.
    /// </param>
    public SessionExpiryException(
        string message,
        string? sessionRef,
        bool needsAttention = true)
        : base(message)
    {
        SessionRef    = sessionRef;
        NeedsAttention = needsAttention;
    }

    /// <summary>
    /// Initialises a new <see cref="SessionExpiryException"/> with an inner exception.
    /// </summary>
    public SessionExpiryException(
        string message,
        string? sessionRef,
        Exception innerException,
        bool needsAttention = true)
        : base(message, innerException)
    {
        SessionRef    = sessionRef;
        NeedsAttention = needsAttention;
    }
}
