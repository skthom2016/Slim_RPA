namespace Tracto.Designer.App.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Tracto.Designer.App.Commands;
using Tracto.Designer.Core.Browser;
using Tracto.Designer.Core.Scripting;

/// <summary>
/// Browser recording settings and control. Start always opens the chosen browser at
/// <see cref="StartUrl"/> with a clean recording profile and records it.
/// </summary>
public sealed class BrowserAdapterViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly BrowserCaptureAdapter _adapter;
    private readonly IBrowserProfileSignIn _signIn;
    private BrowserType _selectedBrowser = BrowserType.Chromium;
    private int _debugPort = 9222;
    private string _startUrl = "https://www.bing.com/";
    private string _status = "Not connected";

    public BrowserAdapterViewModel(BrowserCaptureAdapter adapter, IBrowserProfileSignIn signIn)
    {
        _adapter = adapter;
        _signIn  = signIn;
        ConnectCommand    = new RelayCommand(async _ => await StartAsync(), _ => !_adapter.IsRunning);
        DisconnectCommand = new RelayCommand(async _ => await StopAsync(),  _ => _adapter.IsRunning);
    }

    public ICommand ConnectCommand    { get; }
    public ICommand DisconnectCommand { get; }
    public Array BrowserTypes { get; } = new[] { BrowserType.Chromium, BrowserType.Chrome, BrowserType.Edge };

    public BrowserType SelectedBrowser
    {
        get => _selectedBrowser;
        set { _selectedBrowser = value; OnPropertyChanged(); }
    }

    public int DebugPort
    {
        get => _debugPort;
        set { _debugPort = value; OnPropertyChanged(); }
    }

    public string StartUrl
    {
        get => _startUrl;
        set { _startUrl = value; OnPropertyChanged(); }
    }

    public bool IsConnected => _adapter.IsRunning;

    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    /// <param name="useSignedInProfile">
    /// Record in the machine's signed-in browser, so sites signed in from the toolbar stay
    /// signed in while recording and the recording matches what the segment will meet at run
    /// time. False records in a throwaway profile with nothing signed in.
    /// </param>
    public async Task StartAsync(bool useSignedInProfile = false)
    {
        try
        {
            string? userDataDir = useSignedInProfile
                ? _signIn.ResolveFolder(ScriptSegment.SharedProfile)
                : null;

            Status = useSignedInProfile
                ? $"Launching {_selectedBrowser} in the signed-in browser..."
                : $"Launching {_selectedBrowser}...";

            // Recording always opens its own browser, so there is nothing to set up first.
            await _adapter.StartAsync(new BrowserCaptureOptions(
                _selectedBrowser, _debugPort, null, _startUrl, LaunchBrowser: true, UserDataDir: userDataDir));

            Status = useSignedInProfile
                ? $"Recording {_selectedBrowser} in the signed-in browser (port {_debugPort})"
                : $"Recording {_selectedBrowser} (port {_debugPort})";
        }
        catch (Exception ex)
        {
            Status = $"Failed: {ex.Message}";
        }
        Refresh();
    }

    public async Task StopAsync()
    {
        if (!_adapter.IsRunning) return;
        try
        {
            await _adapter.StopAsync();
            Status = "Disconnected";
        }
        catch (Exception ex)
        {
            Status = $"Stop failed: {ex.Message}";
        }
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsConnected));
        ((RelayCommand)ConnectCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DisconnectCommand).RaiseCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    public void Dispose() => _adapter.Dispose();
}
