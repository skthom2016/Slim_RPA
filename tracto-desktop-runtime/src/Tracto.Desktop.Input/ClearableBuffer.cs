namespace Tracto.Desktop.Input;

/// <summary>
/// Wraps a <see cref="char"/> array and zeroes every element on <see cref="Dispose"/>.
/// </summary>
public sealed class ClearableBuffer : IDisposable
{
    private char[]? _buffer;
    private bool _disposed;

    public ClearableBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new char[capacity];
    }

    /// <summary>Returns a span over the underlying buffer (throws if disposed).</summary>
    public Span<char> Span
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffer.AsSpan();
        }
    }

    /// <summary>Length of the buffer in characters.</summary>
    public int Length
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _buffer!.Length;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_buffer is not null)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _buffer = null;
        }
    }
}
