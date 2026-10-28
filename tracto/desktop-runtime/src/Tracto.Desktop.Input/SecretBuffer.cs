namespace Tracto.Desktop.Input;

/// <summary>
/// Wraps a <see cref="ClearableBuffer"/> to hold sensitive text (passwords, secrets).
/// <para>
/// <see cref="ToString"/> is deliberately overridden to return a redaction placeholder
/// so the content is never accidentally logged or serialised.
/// </para>
/// </summary>
public sealed class SecretBuffer : IDisposable
{
    private readonly ClearableBuffer _inner;

    public SecretBuffer(int capacity)
    {
        _inner = new ClearableBuffer(capacity);
    }

    /// <summary>Returns a span over the secret characters (disposed check delegated to inner buffer).</summary>
    public Span<char> Span => _inner.Span;

    /// <summary>Length of the buffer in characters.</summary>
    public int Length => _inner.Length;

    /// <inheritdoc/>
    public void Dispose() => _inner.Dispose();

    /// <summary>Always returns a redaction placeholder — never the actual content.</summary>
    public override string ToString() => "***REDACTED***";
}
