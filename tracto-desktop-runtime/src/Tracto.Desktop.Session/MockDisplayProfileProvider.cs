namespace Tracto.Desktop.Session;

/// <summary>
/// Test double for <see cref="IDisplayProfileProvider"/> with a settable <see cref="Profile"/>.
/// </summary>
public sealed class MockDisplayProfileProvider : IDisplayProfileProvider
{
    /// <summary>Gets or sets the profile that will be returned by <see cref="GetCurrentProfile"/>.</summary>
    public DisplayProfile Profile { get; set; } = new DisplayProfile(1920, 1080, 96, 96);

    public DisplayProfile GetCurrentProfile() => Profile;
}
