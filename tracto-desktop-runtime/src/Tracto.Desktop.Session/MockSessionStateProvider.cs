namespace Tracto.Desktop.Session;

/// <summary>
/// Test double for <see cref="ISessionStateProvider"/> with a settable <see cref="State"/>.
/// </summary>
public sealed class MockSessionStateProvider : ISessionStateProvider
{
    /// <summary>Gets or sets the state that will be returned by <see cref="GetCurrentState"/>.</summary>
    public SessionState State { get; set; } = SessionState.Interactive;

    public SessionState GetCurrentState() => State;

    public string GetSessionDescription() => $"Mock session state: {State}";
}
