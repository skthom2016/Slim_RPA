namespace Tracto.Desktop.Input;

/// <summary>
/// Test double for ISendInputProvider. Records every call without sending real OS input.
/// </summary>
public sealed class MockSendInputProvider : ISendInputProvider
{
    private readonly List<MouseInput[]> _calls = new();

    /// <summary>All batches passed to SendMouseInput, in call order.</summary>
    public IReadOnlyList<MouseInput[]> Calls => _calls;

    /// <summary>Flat list of every individual MouseInput sent across all calls.</summary>
    public IReadOnlyList<MouseInput> AllInputs => _calls.SelectMany(b => b).ToList();

    public void SendMouseInput(MouseInput[] inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        // Snapshot so callers cannot mutate recorded data.
        _calls.Add((MouseInput[])inputs.Clone());
    }

    public void Clear() => _calls.Clear();
}
