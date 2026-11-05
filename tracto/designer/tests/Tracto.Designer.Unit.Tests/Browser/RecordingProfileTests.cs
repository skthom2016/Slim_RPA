namespace Tracto.Designer.Unit.Tests.Browser;

using FluentAssertions;
using Tracto.Designer.App.ViewModels;
using Tracto.Designer.Core.Browser;
using Tracto.Designer.Core.Scripting;

/// <summary>
/// Recording has to happen in the same browser profile the segment will run in. Recording a
/// signed-in segment in a throwaway profile puts a login page in the recording instead of the
/// page the script will actually meet.
/// </summary>
public class RecordingProfileTests
{
    private sealed class CapturingEngine : IBrowserCaptureEngine
    {
        public BrowserCaptureOptions? Started { get; private set; }
        public bool IsRunning { get; private set; }
        public event EventHandler<BrowserRawEvent>? EventCaptured;

        public Task StartAsync(BrowserCaptureOptions options, CancellationToken ct = default)
        {
            Started = options;
            IsRunning = true;
            EventCaptured?.Invoke(this, null!);   // never raised; keeps the compiler quiet
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    private sealed class FakeSignIn : IBrowserProfileSignIn
    {
        public const string Folder = @"C:\profiles\default";
        public bool IsValidName(string? profileName) => !string.IsNullOrWhiteSpace(profileName);
        public string ResolveFolder(string profileName) => $@"C:\profiles\{profileName}";
        public Task SignInAsync(BrowserType browser, string profileName, string? startUrl, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private static (BrowserAdapterViewModel Vm, CapturingEngine Engine) Build()
    {
        var engine = new CapturingEngine();
        var adapter = new BrowserCaptureAdapter(engine);
        return (new BrowserAdapterViewModel(adapter, new FakeSignIn()), engine);
    }

    [Fact]
    public async Task Recording_A_Signed_In_Segment_Uses_The_Shared_Signed_In_Profile()
    {
        var (vm, engine) = Build();

        await vm.StartAsync(useSignedInProfile: true);

        engine.Started.Should().NotBeNull();
        engine.Started!.UserDataDir.Should().Be(FakeSignIn.Folder,
            "recording must open the same folder the ribbon's Sign in writes to");
    }

    [Fact]
    public async Task Recording_An_Empty_Browser_Segment_Uses_No_Named_Profile()
    {
        var (vm, engine) = Build();

        await vm.StartAsync(useSignedInProfile: false);

        engine.Started!.UserDataDir.Should().BeNull(
            "a segment with no sign-in records in the recorder's own throwaway profile");
    }

    [Fact]
    public async Task Recording_Always_Launches_Its_Own_Browser()
    {
        var (vm, engine) = Build();

        await vm.StartAsync();

        engine.Started!.LaunchBrowser.Should().BeTrue();
    }

    [Fact]
    public void The_Signed_In_Folder_Is_The_One_Every_Signed_In_Segment_Shares()
    {
        var signIn = new FakeSignIn();
        var a = new ScriptSegment { Runtime = "browser", Name = "Read mail" };
        var b = new ScriptSegment { Runtime = "browser", Name = "Send mail" };

        a.UseSignedInProfile = true;
        b.UseSignedInProfile = true;

        signIn.ResolveFolder(a.Profile).Should().Be(signIn.ResolveFolder(b.Profile));
        signIn.ResolveFolder(a.Profile).Should().Be(FakeSignIn.Folder);
    }
}
