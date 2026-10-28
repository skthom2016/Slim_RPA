using Tracto.Desktop.Element;

namespace Tracto.Desktop.Input;

/// <summary>
/// A no-operation <see cref="IKeyInputSimulator"/> used in unit tests where real
/// keyboard injection is neither desired nor possible.
/// </summary>
public sealed class NoOpKeyInputSimulator : IKeyInputSimulator
{
    public Task SelectAllAsync(ElementRef element, CancellationToken ct) => Task.CompletedTask;
    public Task SendCopyAsync(CancellationToken ct) => Task.CompletedTask;
    public Task SendPasteAsync(ElementRef element, CancellationToken ct) => Task.CompletedTask;
}
